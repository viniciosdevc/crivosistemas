using System.Text.Json;
using Crivosistemas.Domain.Audit;
using Crivosistemas.Domain.Estoque;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Estoque;
using Crivosistemas.Infrastructure.Financeiro;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crivosistemas.Infrastructure.Integracoes;

public enum DesfechoIngestao
{
    /// <summary>Processado com sucesso (200).</summary>
    Processado,
    /// <summary>O id do evento já tinha sido processado (200 com duplicado).</summary>
    Duplicado,
    /// <summary>Payload inválido: erro permanente (4xx).</summary>
    Rejeitado,
    /// <summary>Erro inesperado: o crivonft deve repetir (5xx).</summary>
    Falhou,
}

public sealed record ResultadoIngestao(DesfechoIngestao Desfecho, Guid? EventoId, string? Resultado, IReadOnlyList<string> Erros);

public enum ResultadoVenda { Aplicada, IgnoradaMaisAntiga, IgnoradaRemovida }

/// <summary>Recebe os eventos do crivonft: grava o corpo bruto (único por id) e aplica vendas e clientes. Também é usado pela
/// reconciliação, que converte as linhas exportadas no mesmo payload e passa pela mesma regra de upsert.</summary>
public sealed class IngestaoService(AppDbContext db, FinanceiroService financeiro, EstoqueService estoque, ILogger<IngestaoService> logger)
{
    private const int MaxCorpoTexto = 1_000_000;

    public async Task<ResultadoIngestao> ReceberAsync(string corpo, CancellationToken ct)
    {
        var env = PayloadParser.Envelope(corpo);
        if (!env.Ok)
        {
            // Se ao menos o id é um UUID, guarda o evento como rejeitado para aparecer na tela de integrações.
            var id = TentarId(corpo);
            if (id is { } eid) await GuardarRejeitadoAsync(eid, corpo, env.Erros, ct);
            return new ResultadoIngestao(DesfechoIngestao.Rejeitado, id, null, env.Erros);
        }

        var e = env.Valor!;
        var ev = await db.EventosRecebidos.FirstOrDefaultAsync(x => x.EventoId == e.Id, ct);
        if (ev is { Estado: EstadoEvento.Processado }) return new ResultadoIngestao(DesfechoIngestao.Duplicado, e.Id, ev.Resultado, []);

        if (ev is null)
        {
            ev = new EventoRecebido { EventoId = e.Id, Tipo = e.Tipo, OcorridoEm = e.OcorridoEm, Corpo = Limitar(corpo) };
            db.EventosRecebidos.Add(ev);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (EhDuplicado(ex))
            {
                // Corrida: o mesmo id chegou duas vezes ao mesmo tempo. Quem perdeu responde "duplicado" se o outro já terminou.
                db.ChangeTracker.Clear();
                ev = await db.EventosRecebidos.FirstAsync(x => x.EventoId == e.Id, ct);
                if (ev.Estado == EstadoEvento.Processado) return new ResultadoIngestao(DesfechoIngestao.Duplicado, e.Id, ev.Resultado, []);
            }
        }
        else
        {
            ev.Corpo = Limitar(corpo); // reenvio de um evento que falhou/foi rejeitado: vale o corpo mais recente
        }
        return await ProcessarAsync(ev.Id, ct);
    }

    /// <summary>Reaplica um evento já gravado (botão "reprocessar" da tela Integrações). Seguro: a regra de upsert por data protege dados mais novos.</summary>
    public async Task<ResultadoIngestao?> ReprocessarAsync(Guid id, CancellationToken ct)
    {
        if (!await db.EventosRecebidos.AnyAsync(x => x.Id == id, ct)) return null;
        return await ProcessarAsync(id, ct);
    }

    private async Task<ResultadoIngestao> ProcessarAsync(Guid id, CancellationToken ct)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
                var ev = await db.EventosRecebidos.FirstAsync(x => x.Id == id, ct);
                ev.Tentativas++;
                var r = await AplicarEventoAsync(ev, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return r;
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao processar o evento {Id}", id);
            db.ChangeTracker.Clear();
            var ev = await db.EventosRecebidos.FirstAsync(x => x.Id == id, CancellationToken.None);
            ev.Estado = EstadoEvento.Falhou;
            ev.Erro = Cortar(ex.Message, 1900);
            ev.Tentativas++;
            await db.SaveChangesAsync(CancellationToken.None);
            return new ResultadoIngestao(DesfechoIngestao.Falhou, ev.EventoId, null, [ev.Erro]);
        }
    }

    private async Task<ResultadoIngestao> AplicarEventoAsync(EventoRecebido ev, CancellationToken ct)
    {
        var env = PayloadParser.Envelope(ev.Corpo);
        if (!env.Ok) return Rejeitar(ev, env.Erros);
        var e = env.Valor!;
        string resultado;

        switch (e.Tipo)
        {
            case TiposEvento.VendaUpsert:
            {
                var p = PayloadParser.VendaUpsert(e.Dados);
                if (!p.Ok) return Rejeitar(ev, p.Erros);
                ev.Origem = Nomes.Origem(p.Valor!.Origem);
                ev.OrigemId = p.Valor.OrigemId;
                ev.ClienteChave = p.Valor.Cliente?.Chave;
                resultado = DescreverVenda(await AplicarVendaAsync(p.Valor, ct));
                break;
            }
            case TiposEvento.VendaRemovida:
            {
                var p = PayloadParser.VendaRemovida(e.Dados);
                if (!p.Ok) return Rejeitar(ev, p.Erros);
                ev.Origem = Nomes.Origem(p.Valor!.Origem);
                ev.OrigemId = p.Valor.OrigemId;
                await RemoverVendaAsync(p.Valor, e.OcorridoEm, ct);
                resultado = "removida";
                break;
            }
            default:
            {
                var p = PayloadParser.ClienteUpsert(e.Dados);
                if (!p.Ok) return Rejeitar(ev, p.Erros);
                ev.ClienteChave = p.Valor!.Chave;
                await AplicarClienteAsync(p.Valor, sobrescrever: true, ct);
                resultado = "aplicado";
                break;
            }
        }

        ev.Estado = EstadoEvento.Processado;
        ev.ProcessadoEm = DateTime.UtcNow;
        ev.Erro = null;
        ev.Resultado = resultado;
        return new ResultadoIngestao(DesfechoIngestao.Processado, ev.EventoId, resultado, []);
    }

    private static ResultadoIngestao Rejeitar(EventoRecebido ev, IReadOnlyList<string> erros)
    {
        ev.Estado = EstadoEvento.Rejeitado;
        ev.Erro = Cortar(string.Join(" ", erros), 1900);
        ev.ProcessadoEm = DateTime.UtcNow;
        ev.Resultado = null;
        return new ResultadoIngestao(DesfechoIngestao.Rejeitado, ev.EventoId, null, erros);
    }

    private static string DescreverVenda(ResultadoVenda r) => r switch
    {
        ResultadoVenda.Aplicada => "aplicado",
        ResultadoVenda.IgnoradaMaisAntiga => "ignorado: atualizado_em mais antigo que o gravado",
        _ => "ignorado: venda já removida",
    };

    // ── regras de negócio ───────────────────────────────────────────────────

    /// <summary>Upsert por (origem, origem_id). Não chama SaveChanges; quem chama decide a unidade de trabalho.</summary>
    public async Task<ResultadoVenda> AplicarVendaAsync(VendaUpsertPayload p, CancellationToken ct)
    {
        var venda = await db.Vendas.Include(v => v.Itens).FirstOrDefaultAsync(v => v.Origem == p.Origem && v.OrigemId == p.OrigemId, ct);
        if (venda is not null)
        {
            if (venda.Removida) return ResultadoVenda.IgnoradaRemovida;
            if (!RegraUpsert.Aplicar(venda.AtualizadoEmOrigem, p.AtualizadoEm))
            {
                // Dado mais antigo não sobrescreve nada, mas uma venda ainda sem cliente ganha o vínculo (corrige o que já foi gravado sem ele).
                if (venda.ClienteId is null && p.Cliente is not null)
                {
                    var achado = await AplicarClienteAsync(p.Cliente, sobrescrever: false, ct);
                    venda.Cliente = achado;
                    venda.ClienteId = achado.Id;
                }
                return ResultadoVenda.IgnoradaMaisAntiga;
            }
        }

        var cliente = p.Cliente is null ? null : await AplicarClienteAsync(p.Cliente, sobrescrever: true, ct);

        if (venda is null)
        {
            venda = new Venda { Origem = p.Origem, OrigemId = p.OrigemId };
            db.Vendas.Add(venda);
        }
        venda.PedidoId = p.PedidoId;
        venda.DataHora = p.DataHora;
        venda.Status = p.Status;
        venda.StatusNormalizado = p.StatusNormalizado;
        venda.FormaPagamento = p.FormaPagamento;
        venda.Vendedor = p.Vendedor;
        venda.Cliente = cliente;
        venda.ClienteId = cliente?.Id;
        var v = p.Valores;
        venda.TotalBrl = v.TotalBrl; venda.DescontoBrl = v.DescontoBrl; venda.TaxaBrl = v.TaxaBrl; venda.ValorPixBrl = v.ValorPixBrl;
        venda.ValorCambios = v.ValorCambios; venda.CustoBrl = v.CustoBrl; venda.CustoUsd = v.CustoUsd; venda.CustoCambios = v.CustoCambios;
        venda.LucroBrl = v.LucroBrl; venda.MargemPercent = v.MargemPercent;
        venda.CupomCodigo = p.Cupom?.Codigo;
        venda.CupomTipo = p.Cupom?.Tipo;
        venda.AtualizadoEmOrigem = p.AtualizadoEm;
        venda.AtualizadoEm = DateTime.UtcNow;

        db.VendaItens.RemoveRange(venda.Itens);
        venda.Itens.Clear();
        var ordem = 0;
        foreach (var i in p.Itens)
            venda.Itens.Add(new VendaItem { Ordem = ordem++, Descricao = i.Descricao, Quantidade = i.Quantidade, PrecoUsd = i.PrecoUsd, PrecoBrl = i.PrecoBrl, Referencia = i.Referencia });
        if ((venda.Origem is OrigemVenda.Site or OrigemVenda.Nft) && CustoVendaSite.Vale(venda.DataHora)) await ApurarCustoSiteAsync(venda, ct);
        // O PIX da venda entra (ou sai, se ela foi cancelada) direto no financeiro.
        await financeiro.SincronizarVendaAsync(venda, ct);
        return ResultadoVenda.Aplicada;
    }

    private async Task RemoverVendaAsync(VendaRemovidaPayload p, DateTime ocorridoEm, CancellationToken ct)
    {
        var venda = await db.Vendas.FirstOrDefaultAsync(v => v.Origem == p.Origem && v.OrigemId == p.OrigemId, ct);
        if (venda is null)
        {
            // A remoção chegou antes (ou sem) o upsert: guarda a marca para que um upsert atrasado seja ignorado.
            venda = new Venda
            {
                Origem = p.Origem, OrigemId = p.OrigemId, DataHora = ocorridoEm, Status = "removida",
                StatusNormalizado = StatusVenda.Cancelado, AtualizadoEmOrigem = ocorridoEm,
            };
            db.Vendas.Add(venda);
        }
        venda.RemovidaEm ??= ocorridoEm;
        if (ocorridoEm > venda.AtualizadoEmOrigem) venda.AtualizadoEmOrigem = ocorridoEm;
        venda.AtualizadoEm = DateTime.UtcNow;
        if (venda.Origem is OrigemVenda.Site or OrigemVenda.Nft)
        {
            await estoque.TravarAsync(ct);
            await estoque.EstornarVendaAsync(venda.Id, TipoEstoque.Usd, null, ct);
        }
        await financeiro.SincronizarVendaAsync(venda, ct);
    }

    /// <summary>O Gestão é a fonte da verdade do custo: o dólar dos itens vem do crivonft, mas o custo em R$ é o custo médio do estoque daqui,
    /// e a venda paga ou concluída baixa esse dólar (FIFO). Refazer a venda devolve a baixa anterior aos mesmos lotes antes de baixar de novo.
    /// Sem custo médio (estoque vazio) mantém o custo que veio do crivonft. Falta de saldo não recusa a venda: baixa o que há e avisa na observação.</summary>
    private async Task ApurarCustoSiteAsync(Venda venda, CancellationToken ct)
    {
        await estoque.TravarAsync(ct);
        await estoque.EstornarVendaAsync(venda.Id, TipoEstoque.Usd, null, ct);
        var temAviso = venda.Observacao?.StartsWith(CustoVendaSite.AvisoFalta, StringComparison.Ordinal) == true;
        if (temAviso) venda.Observacao = null;
        if (venda.CustoUsd <= 0) return;

        var medio = (await estoque.ResumoAsync(TipoEstoque.Usd, ct)).CustoMedio;
        if (medio <= 0)
        {
            logger.LogWarning("Venda do site {OrigemId} sem custo médio de dólar no estoque: mantido o custo do crivonft.", venda.OrigemId);
            return;
        }
        (venda.CustoBrl, venda.LucroBrl, venda.MargemPercent) = CustoVendaSite.Calcular(venda.TotalBrl, venda.CustoUsd, medio);
        venda.OrigemCusto = OrigemCusto.EstoqueUsd;
        venda.DolarCompraUsado = medio;
        if (!venda.Faturavel) return;

        var faltou = await estoque.BaixarVendaSiteAsync(venda.CustoUsd, venda.Id, ct);
        if (faltou > 0)
        {
            venda.Observacao = $"{CustoVendaSite.AvisoFalta}faltaram US$ {faltou:0.##} para baixar esta venda.";
            logger.LogWarning("Venda do site {OrigemId}: faltaram US$ {Faltou} no estoque de dólar.", venda.OrigemId, faltou);
        }
    }

    /// <summary>Cria ou atualiza o cliente pela chave. Com <paramref name="sobrescrever"/> falso (linhas exportadas) só cria: os eventos são mais completos.</summary>
    public async Task<Cliente> AplicarClienteAsync(ClientePayload p, bool sobrescrever, CancellationToken ct)
    {
        var c = db.Clientes.Local.FirstOrDefault(x => string.Equals(x.Chave, p.Chave, StringComparison.OrdinalIgnoreCase))
            ?? await db.Clientes.FirstOrDefaultAsync(x => x.Chave == p.Chave, ct);
        if (c is null)
        {
            c = new Cliente { Chave = p.Chave, Nickname = p.Nickname, Email = p.Email, IdentidadeId = p.IdentidadeId, Carteira = p.Carteira, CarteiraLoja = p.CarteiraLoja, Favorito = p.Favorito ?? false, CriadoEmOrigem = p.CriadoEm };
            db.Clientes.Add(c);
            return c;
        }
        if (!sobrescrever)
        {
            // Dado exportado não sobrescreve, mas preenche a carteira que ainda faltava e aplica o favorito (o crivonft é o dono).
            if (c.Carteira is null && p.Carteira is not null) c.Carteira = p.Carteira;
            if (c.CarteiraLoja is null && p.CarteiraLoja is not null) c.CarteiraLoja = p.CarteiraLoja;
            if (p.Favorito is { } fav && c.Favorito != fav) c.Favorito = fav;
            return c;
        }
        c.Nickname = p.Nickname;
        if (p.Email is not null) c.Email = p.Email;
        if (p.IdentidadeId is not null) c.IdentidadeId = p.IdentidadeId;
        if (p.Carteira is not null) c.Carteira = p.Carteira; // null nunca apaga uma carteira conhecida
        if (p.CarteiraLoja is not null) c.CarteiraLoja = p.CarteiraLoja;
        if (p.Favorito is { } favorito) c.Favorito = favorito;
        if (p.CriadoEm is not null) c.CriadoEmOrigem = p.CriadoEm;
        c.AtualizadoEm = DateTime.UtcNow;
        return c;
    }

    // ── utilidades ──────────────────────────────────────────────────────────

    private async Task GuardarRejeitadoAsync(Guid id, string corpo, IReadOnlyList<string> erros, CancellationToken ct)
    {
        string tipo = "desconhecido";
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.TryGetProperty("tipo", out var t) && t.ValueKind == JsonValueKind.String) tipo = Cortar(t.GetString() ?? tipo, 30);
        }
        catch (JsonException) { /* sem tipo legível */ }

        var ev = await db.EventosRecebidos.FirstOrDefaultAsync(x => x.EventoId == id, ct);
        if (ev is { Estado: EstadoEvento.Processado }) return;
        if (ev is null)
        {
            ev = new EventoRecebido { EventoId = id, Tipo = tipo, OcorridoEm = DateTime.UtcNow, Corpo = Limitar(corpo) };
            db.EventosRecebidos.Add(ev);
        }
        ev.Estado = EstadoEvento.Rejeitado;
        ev.Erro = Cortar(string.Join(" ", erros), 1900);
        ev.Tentativas++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (EhDuplicado(ex)) { db.ChangeTracker.Clear(); }
    }

    private static Guid? TentarId(string corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("id", out var i)
                && i.ValueKind == JsonValueKind.String && Guid.TryParse(i.GetString(), out var g) ? g : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool EhDuplicado(DbUpdateException e) =>
        e.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true;

    private static string Limitar(string s) => s.Length <= MaxCorpoTexto ? s : s[..MaxCorpoTexto];
    private static string Cortar(string s, int max) => s.Length <= max ? s : s[..max];
}
