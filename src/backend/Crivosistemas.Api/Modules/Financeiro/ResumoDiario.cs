using Crivosistemas.Api.Modules.Projecao;
using Crivosistemas.Domain.Financeiro;
using Crivosistemas.Domain.Projecao;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Financeiro;

public sealed record ResumoDiarioTotaisDto(decimal SaldoAtual, decimal Entradas, decimal Saidas, decimal SaldoFinalProjetado, decimal MenorSaldoProjetado, DateOnly? DiaMenorSaldo,
    decimal MediaFaturamentoDia, decimal MediaLucroDia, decimal ConhecidoEntradas, decimal ConhecidoSaidas);
public sealed record ResumoDiarioDto(string Mes, DateOnly Hoje, int ProjecaoDias, string GranularidadeProjecao, IReadOnlyList<LinhaResumo> Linhas, ResumoDiarioTotaisDto Totais);

/// <summary>Resumo diário de caixa: o realizado vem dos lançamentos confirmados (vendas PIX, câmbios e compras já entram sozinhos);
/// a projeção usa a média dos últimos 30 dias de vendas e lucro (como a página Projeção) mais os lançamentos futuros já conhecidos.</summary>
public static class ResumoDiario
{
    private static readonly int[] Periodos = [15, 30, 90, 120, 365, 1825];
    private const int DiasBase = 30;

    public static async Task<IResult> Obter(AppDbContext db, string? mes, int? projecao, CancellationToken ct)
    {
        var hoje = FusoSp.Hoje();
        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        if (!string.IsNullOrWhiteSpace(mes))
        {
            if (!DateOnly.TryParseExact(mes.Trim() + "-01", "yyyy-MM-dd", out inicioMes)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["mes"] = ["Use o formato AAAA-MM."] });
        }
        var dias = projecao ?? 0;
        if (dias != 0 && !Periodos.Contains(dias)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["projecao"] = ["Use 15, 30, 90, 120, 365 ou 1825 dias."] });
        if (dias > 0) inicioMes = new DateOnly(hoje.Year, hoje.Month, 1); // com projeção a tabela é sempre do mês atual para a frente

        var fimMes = inicioMes.AddMonths(1).AddDays(-1);
        var ultimoReal = fimMes < hoje ? fimMes : hoje;
        var l = db.LancamentosFinanceiros.AsNoTracking().Where(x => x.Status == StatusLancamento.Confirmado);

        var anteriores = await l.Where(x => x.Data < inicioMes).GroupBy(x => x.Tipo).Select(g => new { Tipo = g.Key, Total = g.Sum(x => x.ValorBrl) }).ToListAsync(ct);
        var saldoInicial = anteriores.Where(x => x.Tipo == TipoLancamento.Entrada).Sum(x => x.Total) - anteriores.Where(x => x.Tipo == TipoLancamento.Saida).Sum(x => x.Total);

        var porDia = ultimoReal < inicioMes ? [] : await l.Where(x => x.Data >= inicioMes && x.Data <= ultimoReal)
            .GroupBy(x => new { x.Data, x.Tipo }).Select(g => new { g.Key.Data, g.Key.Tipo, Total = g.Sum(x => x.ValorBrl) }).ToListAsync(ct);
        var realizado = new List<(DateOnly Dia, decimal Entrada, decimal Saida)>();
        for (var d = inicioMes; d <= ultimoReal; d = d.AddDays(1))
            realizado.Add((d, porDia.Where(x => x.Data == d && x.Tipo == TipoLancamento.Entrada).Sum(x => x.Total), porDia.Where(x => x.Data == d && x.Tipo == TipoLancamento.Saida).Sum(x => x.Total)));
        var linhas = RegrasResumoDiario.Agrupar(realizado, saldoInicial, "dia", projetado: false);
        var saldoAtual = linhas.Count > 0 ? linhas[^1].SaldoAcumulado : saldoInicial;

        decimal mediaFat = 0, mediaLuc = 0, conhecidoE = 0, conhecidoS = 0, saldoFinal = saldoAtual, menor = saldoAtual;
        DateOnly? diaMenor = null;
        var gran = RegrasResumoDiario.Granularidade(dias);
        if (dias > 0)
        {
            var inicioProj = hoje.AddDays(1);
            var fimProj = hoje.AddDays(dias);
            var base30 = await ProjecaoEndpoints.VendasPorDiaAsync(db, hoje.AddDays(-DiasBase), hoje.AddDays(-1), ct);
            mediaFat = RegrasProjecao.MediaDiaria(base30.Values.Sum(x => x.Fat), hoje.AddDays(-DiasBase), hoje.AddDays(-1));
            mediaLuc = RegrasProjecao.MediaDiaria(base30.Values.Sum(x => x.Luc), hoje.AddDays(-DiasBase), hoje.AddDays(-1));
            var custoDia = mediaFat - mediaLuc;

            var futuros = await db.LancamentosFinanceiros.AsNoTracking().Where(x => x.Status != StatusLancamento.Cancelado && x.Data >= inicioProj && x.Data <= fimProj)
                .GroupBy(x => new { x.Data, x.Tipo }).Select(g => new { g.Key.Data, g.Key.Tipo, Total = g.Sum(x => x.ValorBrl) }).ToListAsync(ct);
            conhecidoE = futuros.Where(x => x.Tipo == TipoLancamento.Entrada).Sum(x => x.Total);
            conhecidoS = futuros.Where(x => x.Tipo == TipoLancamento.Saida).Sum(x => x.Total);

            var proj = new List<(DateOnly Dia, decimal Entrada, decimal Saida)>();
            for (var d = inicioProj; d <= fimProj; d = d.AddDays(1))
                proj.Add((d, mediaFat + futuros.Where(x => x.Data == d && x.Tipo == TipoLancamento.Entrada).Sum(x => x.Total),
                    custoDia + futuros.Where(x => x.Data == d && x.Tipo == TipoLancamento.Saida).Sum(x => x.Total)));
            var linhasProj = RegrasResumoDiario.Agrupar(proj, saldoAtual, gran, projetado: true);
            linhas.AddRange(linhasProj);
            saldoFinal = linhasProj.Count > 0 ? linhasProj[^1].SaldoAcumulado : saldoAtual;
            var pior = linhasProj.OrderBy(x => x.SaldoAcumulado).FirstOrDefault();
            if (pior is not null) { menor = pior.SaldoAcumulado; diaMenor = pior.Fim; }
        }

        return Results.Ok(new ResumoDiarioDto(inicioMes.ToString("yyyy-MM"), hoje, dias, gran, linhas,
            new ResumoDiarioTotaisDto(saldoAtual, realizado.Sum(x => x.Entrada), realizado.Sum(x => x.Saida), saldoFinal, menor, diaMenor, mediaFat, mediaLuc, conhecidoE, conhecidoS)));
    }
}
