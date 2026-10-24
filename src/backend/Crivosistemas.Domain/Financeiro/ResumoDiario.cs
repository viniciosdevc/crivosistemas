namespace Crivosistemas.Domain.Financeiro;

/// <summary>Uma linha do resumo diário: um dia, uma semana ou um mês, com o saldo acumulado ao fim dela.</summary>
public sealed record LinhaResumo(DateOnly Inicio, DateOnly Fim, decimal Entrada, decimal Saida, decimal Saldo, decimal SaldoAcumulado, bool Projetado);

/// <summary>Resumo diário de caixa (estilo planilha): entrada, saída, saldo do dia e saldo acumulado, com a projeção agrupada
/// por semana ou mês quando o período é longo, para a tela continuar leve.</summary>
public static class RegrasResumoDiario
{
    public const int MaxDiasProjecao = 1830;

    /// <summary>Até 30 dias, dia a dia; até 120, por semana; acima disso, por mês.</summary>
    public static string Granularidade(int dias) => dias <= 30 ? "dia" : dias <= 120 ? "semana" : "mes";

    /// <summary>Primeiro dia do grupo que contém <paramref name="dia"/> (segunda-feira na semana, dia 1 no mês).</summary>
    public static DateOnly InicioDoGrupo(DateOnly dia, string granularidade) => granularidade switch
    {
        "semana" => dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7)),
        "mes" => new DateOnly(dia.Year, dia.Month, 1),
        _ => dia,
    };

    /// <summary>Soma os dias (em ordem) em linhas da granularidade pedida e acumula o saldo a partir de <paramref name="saldoInicial"/>.</summary>
    public static List<LinhaResumo> Agrupar(IEnumerable<(DateOnly Dia, decimal Entrada, decimal Saida)> dias, decimal saldoInicial, string granularidade, bool projetado)
    {
        var linhas = new List<LinhaResumo>();
        var acumulado = saldoInicial;
        foreach (var grupo in dias.OrderBy(d => d.Dia).GroupBy(d => InicioDoGrupo(d.Dia, granularidade)))
        {
            var e = grupo.Sum(d => d.Entrada);
            var s = grupo.Sum(d => d.Saida);
            acumulado += e - s;
            linhas.Add(new LinhaResumo(grupo.Min(d => d.Dia), grupo.Max(d => d.Dia), e, s, e - s, acumulado, projetado));
        }
        return linhas;
    }
}
