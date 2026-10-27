using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Metas;

/// <summary>Meta de lucro de uma semana (segunda a domingo, dia de São Paulo). <see cref="SemanaInicio"/> é sempre uma segunda-feira.
/// Semana sem linha vale <see cref="RegrasMetas.PadraoBrl"/>.</summary>
public class MetaSemanal : Entity
{
    public DateOnly SemanaInicio { get; set; }
    public decimal MetaLucroBrl { get; set; }
    public Guid? AtualizadoPor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

public enum StatusMeta { Batida, UltimoDia, EmAndamento }

/// <summary>Andamento da semana atual em relação à meta. Valores em R$; percentuais de 0 a 100.</summary>
public sealed record ResumoMeta(decimal MetaBrl, decimal LucroBrl, decimal FaltaBrl, decimal ProgressoPercent, decimal RitmoIdealPercent, decimal MediaDiariaBrl,
    decimal ProjecaoFechamentoBrl, int DiasUteisRestantes, decimal NecessarioPorDiaUtilBrl, StatusMeta Status);

public static class RegrasMetas
{
    /// <summary>Meta de uma semana sem meta definida (vale para passadas, atual e futuras).</summary>
    public const decimal PadraoBrl = 1000m;
    public const decimal MaxMetaBrl = 100_000_000m;
    public const int MaxSemanasPorSalvar = 60;

    /// <summary>Segunda-feira da semana que contém <paramref name="dia"/>.</summary>
    public static DateOnly Segunda(DateOnly dia) => dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7));

    /// <summary>Dia da semana de 1 (segunda) a 7 (domingo).</summary>
    public static int DiaDaSemana(DateOnly dia) => ((int)dia.DayOfWeek + 6) % 7 + 1;

    public static ResumoMeta Resumir(decimal meta, decimal lucro, int diaDaSemana)
    {
        var falta = Math.Max(meta - lucro, 0);
        var progresso = meta > 0 ? Math.Min(100m, lucro / meta * 100m) : 0;
        var media = Math.Round(lucro / diaDaSemana, 2, MidpointRounding.AwayFromZero);
        var restantes = diaDaSemana <= 6 ? 7 - diaDaSemana : 0; // dias úteis (segunda a sábado) que ainda faltam, contando hoje
        var status = lucro >= meta ? StatusMeta.Batida : diaDaSemana == 7 ? StatusMeta.UltimoDia : StatusMeta.EmAndamento;
        return new ResumoMeta(meta, lucro, falta, Math.Round(progresso, 2, MidpointRounding.AwayFromZero), Math.Round(diaDaSemana / 7m * 100m, 2, MidpointRounding.AwayFromZero),
            media, Math.Round(media * 7, 2, MidpointRounding.AwayFromZero), restantes,
            restantes > 0 ? Math.Round(falta / restantes, 2, MidpointRounding.AwayFromZero) : 0, status);
    }

    /// <summary>Valida o valor e a data de uma meta. Devolve campo→mensagem.</summary>
    public static string? Validar(DateOnly semanaInicio, decimal valor)
    {
        if (semanaInicio.DayOfWeek != DayOfWeek.Monday) return $"{semanaInicio:dd/MM/yyyy} não é uma segunda-feira.";
        if (valor <= 0 || valor > MaxMetaBrl) return "A meta deve ser maior que zero.";
        return null;
    }
}
