using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Projecao;

/// <summary>Cenário de projeção salvo: a média diária de faturamento e lucro do período base (guardada na hora de salvar, para o cenário não mudar sozinho),
/// um ajuste de crescimento em % e a data final. A projeção começa no dia seguinte ao fim do período base.
/// O caixa inicial é o caixa líquido do momento em que o cenário foi salvo.</summary>
public class CenarioProjecao : Entity
{
    public string Nome { get; set; } = "";
    public DateOnly BaseDe { get; set; }
    public DateOnly BaseAte { get; set; }
    public DateOnly DataFinal { get; set; }
    /// <summary>Ajuste aplicado sobre a média diária (10 = 10% a mais; -20 = 20% a menos).</summary>
    public decimal CrescimentoPercent { get; set; }
    public decimal MediaFaturamentoDia { get; set; }
    public decimal MediaLucroDia { get; set; }
    public decimal CaixaInicialBrl { get; set; }
    public Guid? CriadoPor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public DateOnly InicioProjecao => BaseAte.AddDays(1);
}

public sealed record DiaProjetado(DateOnly Dia, decimal FaturamentoAcumulado, decimal LucroAcumulado);

public static class RegrasProjecao
{
    public const int MaxDiasBase = 400;
    public const int MaxDiasProjecao = 730;

    /// <summary>Média diária do período base: soma ÷ dias do calendário (dias sem venda contam como zero).</summary>
    public static decimal MediaDiaria(decimal total, DateOnly de, DateOnly ate) =>
        Math.Round(total / (ate.DayNumber - de.DayNumber + 1), 2, MidpointRounding.AwayFromZero);

    /// <summary>Projeção simples: cada dia vale a média diária ajustada pelo crescimento; devolve os acumulados dia a dia, de <paramref name="inicio"/> a <paramref name="fim"/>.</summary>
    public static List<DiaProjetado> Projetar(decimal mediaFaturamento, decimal mediaLucro, decimal crescimentoPercent, DateOnly inicio, DateOnly fim)
    {
        var fator = 1 + crescimentoPercent / 100m;
        var dias = new List<DiaProjetado>();
        decimal fat = 0, luc = 0;
        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            fat += mediaFaturamento * fator;
            luc += mediaLucro * fator;
            dias.Add(new DiaProjetado(d, Math.Round(fat, 2, MidpointRounding.AwayFromZero), Math.Round(luc, 2, MidpointRounding.AwayFromZero)));
        }
        return dias;
    }

    /// <summary>Valida as datas do cenário. Devolve campo→mensagem para cada erro.</summary>
    public static Dictionary<string, string> Validar(DateOnly baseDe, DateOnly baseAte, DateOnly dataFinal, DateOnly hoje)
    {
        var erros = new Dictionary<string, string>();
        if (baseDe > baseAte) erros["baseDe"] = "O início do período base deve ser anterior ou igual ao fim.";
        else if (baseAte.DayNumber - baseDe.DayNumber + 1 > MaxDiasBase) erros["baseDe"] = $"O período base pode ter no máximo {MaxDiasBase} dias.";
        if (baseAte > hoje) erros["baseAte"] = "O período base não pode terminar no futuro.";
        if (dataFinal <= baseAte) erros["dataFinal"] = "A data final deve ser depois do fim do período base.";
        else if (dataFinal.DayNumber - baseAte.DayNumber > MaxDiasProjecao) erros["dataFinal"] = $"Projete no máximo {MaxDiasProjecao} dias à frente.";
        return erros;
    }
}
