namespace Crivosistemas.Domain.Precificacao;

public sealed record CustoCalculado(double CustoUsd, double CustoBrl, double CustoCambios, double CustoCambiosBase, double? DolarCompra,
    double PrecoVendaCambios, double PrecoVendaPix, double PixMultiplicador);

public sealed record VendaCalculada(double CustoUsd, double CustoBrl, double CustoBrlSemTaxa, double CustoCambios, double CustoCambiosBase, double TaxaPrataAplicada,
    double? DolarCompraUsado, double ReceitaTotalBrl, double LucroBrl, double LucroCambios, double MargemPercent, double PrecoVendaPix, double PrecoVendaCambios);

/// <summary>Réplica fiel de <c>calculateFromUsd</c>, <c>calculateFromCambios</c> e <c>calculateSale</c> do crivonft (src/lib/calculator.ts),
/// a mesma conta de <c>src/frontend/src/lib/calculator.ts</c>. O servidor recalcula toda venda manual com ela; o que vem da tela é só estimativa.</summary>
public static class CalculoVenda
{
    private static double RoundMoney(double v) => CalculoPreco.Arredondar(v);
    private static double RoundCambios(double v) => double.IsFinite(v) ? Math.Floor(v + 0.5) : 0;

    public static CustoCalculado DeUsd(double custoUsd, ConfigCalculo cfg, IEnumerable<FaixaCalculo> faixas, double? dolarCompra = null)
    {
        var compra = dolarCompra ?? cfg.DolarPadraoCompra;
        var valorK = Math.Max(cfg.ValorKFeira, 0.0001);
        var custoBrl = custoUsd * compra;
        var custoCambiosBase = custoBrl * cfg.MoedasPorK / valorK;
        var custoCambios = custoCambiosBase + cfg.TaxaPrata;

        var precoBaseBrl = custoUsd * cfg.DolarPadraoVenda;
        var precoCambiosBase = precoBaseBrl * cfg.MoedasPorK / valorK;
        var custoTotalCambios = precoCambiosBase + cfg.TaxaPrata;
        var precoVendaCambios = Math.Ceiling(custoTotalCambios * (1 + cfg.MargemVendaCambiosPercent / 100));
        var piso = custoTotalCambios * cfg.ValorKFeira / Math.Max(cfg.MoedasPorK, 0.0001);
        var faixa = CalculoPreco.EncontrarFaixa(custoUsd, faixas);
        var pix = Math.Max(custoUsd * faixa.Multiplicador, piso);

        return new CustoCalculado(RoundMoney(custoUsd), RoundMoney(custoBrl), RoundCambios(custoCambios), RoundCambios(custoCambiosBase), RoundMoney(compra),
            precoVendaCambios, RoundMoney(pix), faixa.Multiplicador);
    }

    /// <summary>Custo informado direto em câmbios: já é o valor final, sem dólar nem taxa de prata.</summary>
    public static CustoCalculado DeCambios(double custoCambios, ConfigCalculo cfg)
    {
        var custoBrl = custoCambios * cfg.ValorKFeira / Math.Max(cfg.MoedasPorK, 0.0001);
        var precoVendaCambios = custoCambios * (1 + cfg.MargemVendaCambiosPercent / 100);
        return new CustoCalculado(0, RoundMoney(custoBrl), RoundCambios(custoCambios), RoundCambios(custoCambios), null, RoundCambios(precoVendaCambios), 0, 0);
    }

    /// <summary>Lucro e margem da venda. Sem <paramref name="custoCambiosManual"/>, o custo vem do dólar.</summary>
    public static VendaCalculada Venda(double custoUsd, double valorVendaPix, double valorVendaCambios, ConfigCalculo cfg, IEnumerable<FaixaCalculo> faixas,
        double? custoCambiosManual = null, double? dolarCompra = null)
    {
        var b = custoCambiosManual is { } cc ? DeCambios(cc, cfg) : DeUsd(custoUsd, cfg, faixas, dolarCompra);
        var k = Math.Max(cfg.MoedasPorK, 0.0001);
        var cambioToBrl = valorVendaCambios * cfg.ValorKFeira / k;
        var receita = valorVendaPix + cambioToBrl;
        var custoBrlTotal = b.CustoCambios * cfg.ValorKFeira / k;
        var custoBrlBase = custoBrlTotal > 0 ? custoBrlTotal : b.CustoBrl;
        var lucroBrl = receita - custoBrlBase;
        var lucroCambios = valorVendaCambios + valorVendaPix * cfg.MoedasPorK / Math.Max(cfg.ValorKFeira, 0.0001) - b.CustoCambios;
        var margem = custoBrlBase > 0 ? lucroBrl / custoBrlBase * 100 : 0;
        return new VendaCalculada(b.CustoUsd, RoundMoney(custoBrlBase), RoundMoney(b.CustoBrl), b.CustoCambios, RoundCambios(b.CustoCambiosBase),
            RoundCambios(b.CustoCambios - b.CustoCambiosBase), b.DolarCompra, RoundMoney(receita), RoundMoney(lucroBrl), RoundCambios(lucroCambios),
            RoundMoney(margem), b.PrecoVendaPix, b.PrecoVendaCambios);
    }
}
