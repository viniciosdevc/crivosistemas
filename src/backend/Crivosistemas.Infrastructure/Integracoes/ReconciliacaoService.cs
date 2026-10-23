using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crivosistemas.Infrastructure.Integracoes;

/// <summary>Reconciliação: puxa vendas e clientes do crivonft (<c>GET /api/export/*</c>) e aplica com a mesma regra de upsert dos eventos.
/// Pedidos manuais ficam "pendentes" e o Worker os executa; a carga inicial roda direto pela linha de comando.</summary>
public sealed class ReconciliacaoService(AppDbContext db, IngestaoService ingestao, ICrivonftClient crivonft, ILogger<ReconciliacaoService> logger)
{
    public const int TamanhoPagina = 1000;
    public const int DiasNoturna = 3;

    /// <summary>Registra um pedido manual para o período. O Worker executa na próxima rodada.</summary>
    public async Task<ReconciliacaoExecucao> SolicitarAsync(DateOnly de, DateOnly ate, Guid? usuario, CancellationToken ct)
    {
        var e = new ReconciliacaoExecucao { Tipo = TipoReconciliacao.Manual, De = de, Ate = ate, SolicitadoPor = usuario };
        db.Reconciliacoes.Add(e);
        await db.SaveChangesAsync(ct);
        return e;
    }

    /// <summary>Pega a execução pendente mais antiga e a marca como em andamento. Null se não há.</summary>
    public async Task<ReconciliacaoExecucao?> ReivindicarPendenteAsync(CancellationToken ct)
    {
        // Execuções "executando" há muito tempo (Worker reiniciado no meio) voltam para a fila.
        var limite = DateTime.UtcNow.AddHours(-2);
        await db.Reconciliacoes.Where(r => r.Estado == EstadoReconciliacao.Executando && r.IniciadoEm < limite)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Estado, EstadoReconciliacao.Pendente), ct);

        var e = await db.Reconciliacoes.Where(r => r.Estado == EstadoReconciliacao.Pendente).OrderBy(r => r.CriadoEm).FirstOrDefaultAsync(ct);
        if (e is null) return null;
        // Reivindicação atômica: se outro processo pegou antes, nenhuma linha é afetada.
        var n = await db.Reconciliacoes.Where(r => r.Id == e.Id && r.Estado == EstadoReconciliacao.Pendente)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Estado, EstadoReconciliacao.Executando).SetProperty(r => r.IniciadoEm, DateTime.UtcNow), ct);
        if (n == 0) return null;
        await db.Entry(e).ReloadAsync(ct);
        return e;
    }

    /// <summary>Garante uma execução noturna por dia (fuso de São Paulo), depois das 03:00. Devolve a criada, ou null.</summary>
    public async Task<ReconciliacaoExecucao?> AgendarNoturnaSeNecessarioAsync(DateTime agoraUtc, CancellationToken ct)
    {
        var local = FusoSp.ParaLocal(agoraUtc);
        if (local.Hour < 3) return null;
        var inicioDia = FusoSp.InicioDoDiaUtc(DateOnly.FromDateTime(local));
        if (await db.Reconciliacoes.AnyAsync(r => r.Tipo == TipoReconciliacao.Noturna && r.CriadoEm >= inicioDia, ct)) return null;

        var hoje = DateOnly.FromDateTime(local);
        var e = new ReconciliacaoExecucao { Tipo = TipoReconciliacao.Noturna, De = hoje.AddDays(-(DiasNoturna - 1)), Ate = hoje };
        db.Reconciliacoes.Add(e);
        await db.SaveChangesAsync(ct);
        return e;
    }

    /// <summary>Carga inicial completa (comando <c>--importar-crivonft [desde]</c>).</summary>
    public async Task<ReconciliacaoExecucao> CargaInicialAsync(DateOnly? desde, CancellationToken ct)
    {
        var e = new ReconciliacaoExecucao { Tipo = TipoReconciliacao.CargaInicial, De = desde, Estado = EstadoReconciliacao.Executando, IniciadoEm = DateTime.UtcNow };
        db.Reconciliacoes.Add(e);
        await db.SaveChangesAsync(ct);
        await ExecutarAsync(e, ct);
        return e;
    }

    /// <summary>Executa uma reconciliação já reivindicada. Nunca lança: o resultado fica na linha.</summary>
    public async Task ExecutarAsync(ReconciliacaoExecucao e, CancellationToken ct)
    {
        try
        {
            if (!crivonft.ExportConfigurado) throw new CrivonftException("Integração com o crivonft não configurada (BaseUrl e ExportToken).");

            // Clientes primeiro, para as vendas já encontrarem a chave.
            for (var offset = 0; ; offset += TamanhoPagina)
            {
                var pag = await crivonft.ExportarClientesAsync(TamanhoPagina, offset, ct);
                foreach (var linha in pag.Data)
                {
                    e.ClientesLidos++;
                    var c = ImportacaoMapper.Cliente(linha);
                    if (!c.Ok) continue;
                    var existia = await db.Clientes.AnyAsync(x => x.Chave == c.Valor!.Chave, ct) || db.Clientes.Local.Any(x => string.Equals(x.Chave, c.Valor!.Chave, StringComparison.OrdinalIgnoreCase));
                    await ingestao.AplicarClienteAsync(c.Valor!, sobrescrever: false, ct);
                    if (!existia) e.ClientesCriados++;
                }
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                db.Attach(e);
                if (pag.Data.Count < TamanhoPagina || offset + pag.Data.Count >= pag.Total) break;
            }

            for (var offset = 0; ; offset += TamanhoPagina)
            {
                var pag = await crivonft.ExportarVendasAsync(e.De, e.Ate, TamanhoPagina, offset, ct);
                foreach (var linha in pag.Data)
                {
                    e.VendasLidas++;
                    var v = ImportacaoMapper.Venda(linha, out var ignorada);
                    if (ignorada is not null || !v.Ok) continue;
                    if (await ingestao.AplicarVendaAsync(v.Valor!, ct) == ResultadoVenda.Aplicada) e.VendasAplicadas++;
                }
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                db.Attach(e);
                if (pag.Data.Count < TamanhoPagina || offset + pag.Data.Count >= pag.Total) break;
            }

            e.Estado = EstadoReconciliacao.Concluida;
            e.Erro = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reconciliação {Id} falhou", e.Id);
            e.Estado = EstadoReconciliacao.Falhou;
            e.Erro = ex.Message.Length <= 1900 ? ex.Message : ex.Message[..1900];
        }
        e.ConcluidoEm = DateTime.UtcNow;
        db.Update(e);
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
