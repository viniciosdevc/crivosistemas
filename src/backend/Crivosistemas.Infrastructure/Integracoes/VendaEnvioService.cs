using Crivosistemas.Domain.Precificacao;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crivosistemas.Infrastructure.Integracoes;

/// <summary>Espelha no crivonft as vendas manuais lançadas aqui (PUT /api/integracoes/gestao/vendas, idempotente por external_id "dash:{id}").
/// O crivonft só grava a linha de venda: estoque e caixa já foram acertados aqui, então ele não debita nem lança nada de novo.</summary>
public sealed class VendaEnvioService(AppDbContext db, ICrivonftClient crivonft, ILogger<VendaEnvioService> logger)
{
    public const string Rota = "/api/integracoes/gestao/vendas";
    private const int Lote = 20;

    /// <summary>Marca a venda como pendente de envio (cria a linha na primeira vez). Chamar na mesma transação que mudou a venda; não salva.</summary>
    public static async Task EnfileirarAsync(AppDbContext db, Guid vendaId, CancellationToken ct)
    {
        var e = await db.VendaEnvios.FirstOrDefaultAsync(x => x.VendaId == vendaId, ct);
        if (e is null) { db.VendaEnvios.Add(new VendaEnvio { VendaId = vendaId }); return; }
        e.Estado = EstadoEnvio.Pendente;
        e.Tentativas = 0;
        e.ProximaTentativaEm = DateTime.UtcNow;
        e.UltimoErro = null;
    }

    /// <summary>Recoloca na fila todas as vendas manuais (carga inicial das que já existiam e reenvio depois de uma falha). Devolve quantas.</summary>
    public async Task<int> EnfileirarTodasAsync(CancellationToken ct)
    {
        var ids = await db.Vendas.Where(v => v.Origem == OrigemVenda.Manual).Select(v => v.Id).ToListAsync(ct);
        foreach (var id in ids) await EnfileirarAsync(db, id, ct);
        await db.SaveChangesAsync(ct);
        return ids.Count;
    }

    /// <summary>Envia o que está vencido (até um lote). Devolve quantas foram entregues.</summary>
    public async Task<int> ProcessarAsync(CancellationToken ct)
    {
        if (!crivonft.EnvioConfigurado) return 0;
        var agora = DateTime.UtcNow;
        var fila = await db.VendaEnvios.Where(x => x.Estado == EstadoEnvio.Pendente && x.ProximaTentativaEm <= agora)
            .OrderBy(x => x.ProximaTentativaEm).Take(Lote).ToListAsync(ct);
        var enviadas = 0;
        foreach (var envio in fila)
        {
            ct.ThrowIfCancellationRequested();
            var venda = await db.Vendas.AsNoTracking().Include(v => v.Cliente).Include(v => v.Itens).FirstOrDefaultAsync(v => v.Id == envio.VendaId, ct);
            if (venda is null) { envio.Estado = EstadoEnvio.Substituido; continue; }

            envio.Tentativas++;
            envio.UltimaTentativaEm = DateTime.UtcNow;
            try
            {
                var r = await crivonft.ChamarGestaoAsync(HttpMethod.Put, Rota, VendaEnvioRegras.Corpo(venda), ct);
                envio.UltimoStatusHttp = r.StatusHttp;
                if (r.Sucesso)
                {
                    // Se a venda mudou durante o envio, EnfileirarAsync já a deixou pendente; o próximo ciclo manda a versão nova.
                    var atual = await db.Vendas.AsNoTracking().Where(v => v.Id == venda.Id).Select(v => v.AtualizadoEmOrigem).FirstAsync(ct);
                    envio.EnviadoAtualizadoEm = venda.AtualizadoEmOrigem;
                    envio.EnviadoEm = DateTime.UtcNow;
                    envio.UltimoErro = null;
                    if (atual > venda.AtualizadoEmOrigem) envio.ProximaTentativaEm = DateTime.UtcNow;
                    else envio.Estado = EstadoEnvio.Enviado;
                    enviadas++;
                }
                else
                {
                    // 404 = loja ainda sem a rota (falta o Deploy do crivonft): repete em vez de desistir.
                    Falha(envio, $"HTTP {r.StatusHttp}: {Cortar(r.Corpo, 500)}", EnvioRegras.ErroPermanente(r.StatusHttp) && r.StatusHttp != 404);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                envio.UltimoStatusHttp = (ex as CrivonftException)?.StatusHttp;
                Falha(envio, ex.Message, permanente: false);
            }
            if (envio.Estado == EstadoEnvio.Falhou) logger.LogWarning("Envio da venda {Venda} ao crivonft desistiu: {Erro}", envio.VendaId, envio.UltimoErro);
        }
        await db.SaveChangesAsync(CancellationToken.None);
        return enviadas;
    }

    private static void Falha(VendaEnvio e, string erro, bool permanente)
    {
        e.UltimoErro = Cortar(erro, 1900);
        if (permanente || e.Tentativas >= EnvioRegras.MaxTentativas) { e.Estado = EstadoEnvio.Falhou; return; }
        e.ProximaTentativaEm = DateTime.UtcNow + EnvioRegras.Atraso(e.Tentativas);
    }

    private static string Cortar(string? s, int max) => s is null ? "" : s.Length <= max ? s : s[..max];
}
