using Crivosistemas.Domain.Cambios;
using Crivosistemas.Domain.Estoque;
using Crivosistemas.Domain.Financeiro;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Infrastructure.Financeiro;

/// <summary>Mantém os lançamentos automáticos em dia com a origem: o PIX de toda venda faturável entra como entrada confirmada
/// e cada compra de dólar do estoque sai como "Compra de USD" (como o crivonft faz em vendas/actions.ts e estoque/actions.ts).
/// As chamadas são idempotentes (um lançamento por venda e por lote) e não chamam SaveChanges: quem chama decide a unidade de trabalho.</summary>
public sealed class FinanceiroService(AppDbContext db)
{
    private async Task<LancamentoFinanceiro?> PorVendaAsync(Guid vendaId, CancellationToken ct) =>
        db.LancamentosFinanceiros.Local.FirstOrDefault(l => l.VendaId == vendaId)
        ?? await db.LancamentosFinanceiros.FirstOrDefaultAsync(l => l.VendaId == vendaId, ct);

    private async Task<LancamentoFinanceiro?> PorLoteAsync(Guid loteId, CancellationToken ct) =>
        db.LancamentosFinanceiros.Local.FirstOrDefault(l => l.LoteEstoqueId == loteId)
        ?? await db.LancamentosFinanceiros.FirstOrDefaultAsync(l => l.LoteEstoqueId == loteId, ct);

    /// <summary>Venda faturável com PIX tem uma entrada confirmada com o valor do PIX; qualquer outra situação (pendente, cancelada,
    /// removida, sem PIX) deixa a entrada cancelada. Se a venda voltar a valer, a mesma entrada volta.</summary>
    public async Task SincronizarVendaAsync(Venda venda, CancellationToken ct)
    {
        var lanc = await PorVendaAsync(venda.Id, ct);
        var vale = venda.Faturavel && venda.ValorPixBrl > 0;
        var agora = DateTime.UtcNow;

        if (!vale)
        {
            if (lanc is null || lanc.Status == StatusLancamento.Cancelado) return;
            lanc.Status = StatusLancamento.Cancelado;
            lanc.MotivoCancelamento = venda.Removida ? "Venda removida na origem." : $"Venda {venda.Status}.".Trim();
            lanc.CanceladoEm = lanc.AtualizadoEm = agora;
            return;
        }

        var item = venda.Itens.OrderBy(i => i.Ordem).FirstOrDefault()?.Descricao
            ?? await db.VendaItens.Where(i => i.VendaId == venda.Id).OrderBy(i => i.Ordem).Select(i => i.Descricao).FirstOrDefaultAsync(ct);
        var nick = venda.Cliente?.Nickname
            ?? (venda.ClienteId is { } cid ? await db.Clientes.Where(c => c.Id == cid).Select(c => c.Nickname).FirstOrDefaultAsync(ct) : null);

        if (lanc is null)
        {
            lanc = new LancamentoFinanceiro
            {
                Tipo = TipoLancamento.Entrada, Origem = OrigemLancamento.Venda, Categoria = RegrasFinanceiro.CategoriaVendaPix,
                VendaId = venda.Id, CriadoPor = venda.CriadoPor, CriadoEm = agora,
            };
            db.LancamentosFinanceiros.Add(lanc);
        }
        if (lanc.Status != StatusLancamento.Confirmado) lanc.ConfirmadoEm = agora;
        lanc.Status = StatusLancamento.Confirmado;
        lanc.MotivoCancelamento = null;
        lanc.CanceladoEm = null;
        lanc.ValorBrl = venda.ValorPixBrl;
        lanc.Data = FusoSp.Dia(venda.DataHora);
        lanc.Descricao = RegrasFinanceiro.DescricaoVendaPix(item, nick);
        lanc.AtualizadoEm = agora;
    }

    /// <summary>Venda de câmbios por R$ (não estornada) é uma entrada confirmada; estornar o movimento cancela a entrada.</summary>
    public async Task SincronizarVendaCambiosAsync(MovimentoCambios mov, CancellationToken ct)
    {
        if (mov.Tipo != TipoMovimentoCambios.Venda) return;
        var lanc = db.LancamentosFinanceiros.Local.FirstOrDefault(l => l.MovimentoCambiosId == mov.Id)
            ?? await db.LancamentosFinanceiros.FirstOrDefaultAsync(l => l.MovimentoCambiosId == mov.Id, ct);
        var agora = DateTime.UtcNow;
        var valor = mov.ValorBrl ?? 0;

        if (mov.EstornadoEm is not null || valor <= 0)
        {
            if (lanc is null || lanc.Status == StatusLancamento.Cancelado) return;
            lanc.Status = StatusLancamento.Cancelado;
            lanc.MotivoCancelamento = "Venda de câmbios estornada.";
            lanc.CanceladoEm = lanc.AtualizadoEm = agora;
            return;
        }

        if (lanc is null)
        {
            string? nick = null;
            if (mov.ClienteId is { } cid)
                nick = db.Clientes.Local.FirstOrDefault(c => c.Id == cid)?.Nickname
                    ?? await db.Clientes.Where(c => c.Id == cid).Select(c => c.Nickname).FirstOrDefaultAsync(ct);
            lanc = new LancamentoFinanceiro
            {
                Tipo = TipoLancamento.Entrada, Origem = OrigemLancamento.VendaCambios, Categoria = RegrasFinanceiro.CategoriaVendaCambios,
                MovimentoCambiosId = mov.Id, CriadoPor = mov.UserId, CriadoEm = agora, ConfirmadoEm = agora,
                Descricao = RegrasFinanceiro.DescricaoVendaCambios(Math.Abs(mov.Quantidade), nick),
            };
            db.LancamentosFinanceiros.Add(lanc);
        }
        lanc.Status = StatusLancamento.Confirmado;
        lanc.MotivoCancelamento = null;
        lanc.CanceladoEm = null;
        lanc.ValorBrl = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
        lanc.Data = FusoSp.Dia(mov.DataHora);
        lanc.AtualizadoEm = agora;
    }

    /// <summary>Compra de dólar com valor em R$ vira uma saída confirmada; editar a compra atualiza a saída e cancelar a compra a cancela.
    /// Lotes de ajuste ou importados do crivonft não geram lançamento (o dinheiro não saiu aqui).</summary>
    public async Task SincronizarCompraUsdAsync(LoteEstoque lote, CancellationToken ct)
    {
        if (lote.Tipo != TipoEstoque.Usd || lote.Origem != OrigemLote.Compra) return;
        var lanc = await PorLoteAsync(lote.Id, ct);
        var agora = DateTime.UtcNow;

        if (lote.Cancelado || lote.CustoTotal <= 0)
        {
            if (lanc is null || lanc.Status == StatusLancamento.Cancelado) return;
            lanc.Status = StatusLancamento.Cancelado;
            lanc.MotivoCancelamento = lote.Cancelado ? $"Compra cancelada no estoque{(string.IsNullOrWhiteSpace(lote.MotivoCancelamento) ? "" : $": {lote.MotivoCancelamento}")}." : "Compra sem valor em R$.";
            if (lanc.MotivoCancelamento.Length > 300) lanc.MotivoCancelamento = lanc.MotivoCancelamento[..300];
            lanc.CanceladoEm = lanc.AtualizadoEm = agora;
            return;
        }

        if (lanc is null)
        {
            lanc = new LancamentoFinanceiro
            {
                Tipo = TipoLancamento.Saida, Origem = OrigemLancamento.CompraUsd, Categoria = RegrasFinanceiro.CategoriaCompraUsd,
                LoteEstoqueId = lote.Id, CriadoPor = lote.CriadoPor, CriadoEm = agora, ConfirmadoEm = agora,
            };
            db.LancamentosFinanceiros.Add(lanc);
        }
        lanc.Status = StatusLancamento.Confirmado;
        lanc.MotivoCancelamento = null;
        lanc.CanceladoEm = null;
        lanc.ValorBrl = Math.Round(lote.CustoTotal, 2, MidpointRounding.AwayFromZero);
        lanc.Data = lote.DataCompra;
        lanc.Descricao = RegrasFinanceiro.DescricaoCompraUsd(lote.Quantidade);
        lanc.AtualizadoEm = agora;
    }

    /// <summary>Gera os lançamentos de tudo que já existia antes do financeiro (vendas com PIX e compras de dólar). Idempotente.</summary>
    public async Task<(int Vendas, int Compras)> SincronizarTudoAsync(CancellationToken ct)
    {
        var vendas = await db.Vendas.Include(v => v.Itens).Include(v => v.Cliente).Where(v => v.ValorPixBrl > 0)
            .Where(v => !db.LancamentosFinanceiros.Any(l => l.VendaId == v.Id)).Where(VendaConsultas.Faturavel).ToListAsync(ct);
        foreach (var v in vendas) await SincronizarVendaAsync(v, ct);

        var lotes = await db.LotesEstoque.Where(l => l.Tipo == TipoEstoque.Usd && l.Origem == OrigemLote.Compra && l.CanceladoEm == null && l.CustoTotal > 0)
            .Where(l => !db.LancamentosFinanceiros.Any(x => x.LoteEstoqueId == l.Id)).ToListAsync(ct);
        foreach (var l in lotes) await SincronizarCompraUsdAsync(l, ct);

        var movs = await db.MovimentosCambios.Where(m => m.Tipo == TipoMovimentoCambios.Venda && m.EstornadoEm == null && m.ValorBrl > 0)
            .Where(m => !db.LancamentosFinanceiros.Any(x => x.MovimentoCambiosId == m.Id)).ToListAsync(ct);
        foreach (var m in movs) await SincronizarVendaCambiosAsync(m, ct);
        await db.SaveChangesAsync(ct);
        return (vendas.Count + movs.Count, lotes.Count);
    }
}
