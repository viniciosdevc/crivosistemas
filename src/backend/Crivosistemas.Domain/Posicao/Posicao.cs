using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Posicao;

/// <summary>Posição do dia: o que o operador conferiu de verdade (caixa em R$, dólares, câmbios, esmeraldas) ao lado do que o sistema calculava naquele momento.
/// Uma linha por dia; informar de novo no mesmo dia substitui. Valor nulo = não conferido. Não mexe nos saldos: a diferença é só um aviso.</summary>
public class PosicaoDiaria : Entity
{
    public DateOnly Data { get; set; }
    public decimal? BrlInformado { get; set; }
    public decimal? UsdInformado { get; set; }
    public decimal? CambiosInformado { get; set; }
    public decimal? EsmeraldasInformado { get; set; }
    public decimal BrlSistema { get; set; }
    public decimal UsdSistema { get; set; }
    public decimal CambiosSistema { get; set; }
    public decimal EsmeraldasSistema { get; set; }
    /// <summary>Quanto o ajuste do dia mexeu em cada saldo (acumulado se a posição foi informada mais de uma vez). Nulo = não ajustado.</summary>
    public decimal? AjusteBrl { get; set; }
    public decimal? AjusteUsd { get; set; }
    public decimal? AjusteCambios { get; set; }
    public decimal? AjusteEsmeraldas { get; set; }
    /// <summary>Saldos depois do ajuste (o que vale para o total do dia).</summary>
    public decimal BrlFinal { get; set; }
    public decimal UsdFinal { get; set; }
    public decimal CambiosFinal { get; set; }
    public decimal EsmeraldasFinal { get; set; }
    /// <summary>Valoração do dia: preço médio de venda do dólar (R$/US$), R$ por câmbio e R$ por esmeralda.</summary>
    public decimal PrecoUsd { get; set; }
    public decimal BrlPorCambio { get; set; }
    public decimal BrlPorEsmeralda { get; set; }
    /// <summary>Custo médio de compra do dólar (R$/US$) no momento.</summary>
    public decimal CustoMedioUsd { get; set; }
    public string? Observacao { get; set; }
    public Guid? CriadoPor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

public static class RegrasPosicao
{
    /// <summary>Total em R$ de um dia: conta + dólar ao preço médio de venda + câmbios ao valor do K + esmeraldas ao custo em R$.</summary>
    public static decimal TotalBrl(decimal brl, decimal usd, decimal cambios, decimal esmeraldas, decimal precoUsd, decimal brlPorCambio, decimal brlPorEsmeralda) =>
        Math.Round(brl + usd * precoUsd + cambios * brlPorCambio + esmeraldas * brlPorEsmeralda, 2, MidpointRounding.AwayFromZero);
}
