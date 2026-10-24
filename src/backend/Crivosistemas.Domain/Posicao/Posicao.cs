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
    /// <summary>Custo médio do dólar (R$/US$) no momento; serve para somar o dólar ao caixa no total operacional.</summary>
    public decimal CustoMedioUsd { get; set; }
    public string? Observacao { get; set; }
    public Guid? CriadoPor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

public static class RegrasPosicao
{
    /// <summary>Diferença informado − sistema; nula quando o saldo não foi conferido.</summary>
    public static decimal? Diferenca(decimal? informado, decimal sistema) => informado is null ? null : informado.Value - sistema;

    /// <summary>Caixa em R$ mais o dólar ao custo médio. Usa o valor conferido; sem conferência, o do sistema. Câmbios e esmeraldas ficam de fora (outra unidade).</summary>
    public static decimal TotalOperacionalBrl(decimal? brlInformado, decimal brlSistema, decimal? usdInformado, decimal usdSistema, decimal custoMedioUsd) =>
        Math.Round((brlInformado ?? brlSistema) + (usdInformado ?? usdSistema) * custoMedioUsd, 2, MidpointRounding.AwayFromZero);
}
