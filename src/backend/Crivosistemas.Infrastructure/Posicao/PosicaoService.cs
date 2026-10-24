using Crivosistemas.Domain.Cambios;
using Crivosistemas.Domain.Estoque;
using Crivosistemas.Domain.Financeiro;
using Crivosistemas.Domain.Posicao;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Cambios;
using Crivosistemas.Infrastructure.Estoque;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Infrastructure.Posicao;

/// <summary>Patrimônio de agora, como o <c>getPatrimonioAtual</c> do crivonft: câmbios ao valor do K da feira, dólar ao preço médio de venda,
/// esmeraldas ao custo médio em US$ × dólar de venda e a conta em R$ do Financeiro.</summary>
public sealed record Patrimonio(
    decimal CambiosSaldo, decimal BrlPorCambio, decimal CambiosBrl,
    decimal UsdSaldo, decimal UsdCustoMedio, decimal UsdPrecoMedioVenda, int UsdVendas, decimal UsdBrl,
    decimal EsmeraldasSaldo, decimal EsmeraldaCustoUsd, decimal BrlPorEsmeralda, decimal EsmeraldasBrl,
    decimal ContaBrl, decimal TotalEntradas, decimal TotalSaidas, decimal PendenteReceber, decimal PendentePagar,
    decimal TotalBrl, decimal CaixaLiquido, decimal LiquidezPercent);

public sealed record AjusteSolicitado(decimal? Brl, decimal? Usd, decimal? Cambios, decimal? Esmeraldas, decimal? CotacaoUsd, string? Observacao);

public sealed class PosicaoService(AppDbContext db, EstoqueService estoque, CambiosService cambios)
{
    public async Task<Patrimonio> AtualAsync(CancellationToken ct)
    {
        var cfg = await db.PrecificacaoConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var brlPorCambio = cfg is null ? 0 : cfg.ValorKFeira / Math.Max(cfg.MoedasPorK, 0.0001m);
        var dolarVenda = cfg?.DolarPadraoVenda ?? 0;

        var cambiosSaldo = await cambios.ObterSaldoAsync(ct);
        var usd = await estoque.ResumoAsync(TipoEstoque.Usd, ct);
        var esm = await estoque.ResumoAsync(TipoEstoque.Esmeralda, ct);

        // Preço médio de venda do dólar: receita (PIX + câmbios convertidos) ÷ US$ consumido do estoque nas vendas não canceladas.
        var v = await db.Vendas.AsNoTracking()
            .Where(x => x.RemovidaEm == null && x.StatusNormalizado != StatusVenda.Cancelado && x.StatusNormalizado != StatusVenda.Reembolsado
                && x.OrigemCusto == OrigemCusto.EstoqueUsd && x.CustoUsd > 0 && x.ValorPixBrl + x.ValorCambios > 0)
            .GroupBy(_ => 1).Select(g => new { Pix = g.Sum(x => x.ValorPixBrl), Cambios = g.Sum(x => x.ValorCambios), Usd = g.Sum(x => x.CustoUsd), N = g.Count() })
            .FirstOrDefaultAsync(ct);
        var precoMedio = v is { Usd: > 0 } ? (v.Pix + v.Cambios * brlPorCambio) / v.Usd : dolarVenda;

        var somas = await db.LancamentosFinanceiros.AsNoTracking().Where(l => l.Status == StatusLancamento.Confirmado).GroupBy(l => l.Tipo)
            .Select(g => new { Tipo = g.Key, Total = g.Sum(l => l.ValorBrl) }).ToListAsync(ct);
        var entradas = somas.FirstOrDefault(s => s.Tipo == TipoLancamento.Entrada)?.Total ?? 0;
        var saidas = somas.FirstOrDefault(s => s.Tipo == TipoLancamento.Saida)?.Total ?? 0;
        var pend = await db.LancamentosFinanceiros.AsNoTracking().Where(l => l.Status == StatusLancamento.Pendente).GroupBy(l => l.Tipo)
            .Select(g => new { Tipo = g.Key, Total = g.Sum(l => l.ValorBrl) }).ToListAsync(ct);

        var conta = entradas - saidas;
        var brlPorEsm = esm.CustoMedio * dolarVenda;
        var cambiosBrl = cambiosSaldo * brlPorCambio;
        var usdBrl = usd.Saldo * precoMedio;
        var esmBrl = esm.Saldo * brlPorEsm;
        var total = cambiosBrl + usdBrl + esmBrl + conta;
        var caixa = conta + Math.Max(0, cambiosBrl);
        return new Patrimonio(cambiosSaldo, brlPorCambio, cambiosBrl, usd.Saldo, usd.CustoMedio, precoMedio, v?.N ?? 0, usdBrl,
            esm.Saldo, esm.CustoMedio, brlPorEsm, esmBrl, conta, entradas, saidas,
            pend.FirstOrDefault(x => x.Tipo == TipoLancamento.Entrada)?.Total ?? 0, pend.FirstOrDefault(x => x.Tipo == TipoLancamento.Saida)?.Total ?? 0,
            total, caixa, total > 0 ? caixa / total * 100 : 0);
    }

    /// <summary>Registra a posição de hoje e ajusta os saldos do sistema para o que foi informado (câmbios, dólar, esmeraldas e conta em R$).
    /// Campo em branco não é ajustado. Nada é apagado: informar de novo no mesmo dia ajusta só a diferença que sobrou, e o ajuste do dia acumula.</summary>
    public async Task<PosicaoDiaria> RegistrarAsync(AjusteSolicitado req, Guid userId, CancellationToken ct)
    {
        await cambios.TravarAsync(ct);
        await estoque.TravarAsync(ct);
        var hoje = FusoSp.Hoje();
        var antes = await AtualAsync(ct);
        var agora = DateTime.UtcNow;
        var obs = $"Ajuste de posição {hoje:dd/MM/yyyy}";

        var p = await db.PosicoesDiarias.FirstOrDefaultAsync(x => x.Data == hoje, ct);
        if (p is null)
        {
            db.PosicoesDiarias.Add(p = new PosicaoDiaria
            {
                Data = hoje, CriadoPor = userId, CriadoEm = agora,
                BrlSistema = antes.ContaBrl, UsdSistema = antes.UsdSaldo, CambiosSistema = antes.CambiosSaldo, EsmeraldasSistema = antes.EsmeraldasSaldo,
            });
        }

        if (req.Cambios is { } c)
        {
            c = Math.Round(c, 2, MidpointRounding.AwayFromZero);
            var d = c - antes.CambiosSaldo;
            if (Math.Abs(d) >= 0.005m) { await cambios.LancarAsync(TipoMovimentoCambios.Ajuste, d, userId, observacao: obs, ct: ct); p.AjusteCambios = (p.AjusteCambios ?? 0) + d; }
            p.CambiosInformado = c;
        }
        if (req.Usd is { } u)
        {
            u = Math.Round(u, 8, MidpointRounding.AwayFromZero);
            var d = u - antes.UsdSaldo;
            if (Math.Abs(d) >= 0.000001m)
            {
                await estoque.AjustarAsync(TipoEstoque.Usd, d > 0, Math.Abs(d), d > 0 ? req.CotacaoUsd : null, obs, userId, ct);
                p.AjusteUsd = (p.AjusteUsd ?? 0) + d;
            }
            p.UsdInformado = u;
        }
        if (req.Esmeraldas is { } e)
        {
            e = Math.Round(e, 8, MidpointRounding.AwayFromZero);
            var d = e - antes.EsmeraldasSaldo;
            if (Math.Abs(d) >= 0.000001m)
            {
                await estoque.AjustarAsync(TipoEstoque.Esmeralda, d > 0, Math.Abs(d), null, obs, userId, ct);
                p.AjusteEsmeraldas = (p.AjusteEsmeraldas ?? 0) + d;
            }
            p.EsmeraldasInformado = e;
        }
        if (req.Brl is { } b)
        {
            b = Math.Round(b, 2, MidpointRounding.AwayFromZero);
            var d = b - antes.ContaBrl;
            if (Math.Abs(d) >= 0.005m)
            {
                db.LancamentosFinanceiros.Add(new LancamentoFinanceiro
                {
                    Tipo = d > 0 ? TipoLancamento.Entrada : TipoLancamento.Saida, Status = StatusLancamento.Confirmado, Origem = OrigemLancamento.Manual,
                    Categoria = d > 0 ? "Ajuste positivo" : "Ajuste negativo", Descricao = obs, ValorBrl = Math.Abs(d), Data = hoje,
                    CriadoPor = userId, CriadoEm = agora, AtualizadoEm = agora, ConfirmadoEm = agora,
                });
                p.AjusteBrl = (p.AjusteBrl ?? 0) + d;
            }
            p.BrlInformado = b;
        }

        // Saldos finais e valoração do dia (congelados para o histórico "dia a dia").
        await db.SaveChangesAsync(ct);
        var depois = await AtualAsync(ct);
        p.BrlFinal = depois.ContaBrl; p.UsdFinal = depois.UsdSaldo; p.CambiosFinal = depois.CambiosSaldo; p.EsmeraldasFinal = depois.EsmeraldasSaldo;
        p.PrecoUsd = depois.UsdPrecoMedioVenda; p.BrlPorCambio = depois.BrlPorCambio; p.BrlPorEsmeralda = depois.BrlPorEsmeralda; p.CustoMedioUsd = depois.UsdCustoMedio;
        p.Observacao = string.IsNullOrWhiteSpace(req.Observacao) ? p.Observacao : req.Observacao.Trim();
        p.AtualizadoEm = agora;
        return p;
    }
}
