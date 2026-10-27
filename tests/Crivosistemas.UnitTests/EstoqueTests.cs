using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Estoque;
using Crivosistemas.Domain.Precificacao;

namespace Crivosistemas.UnitTests;

public sealed class RegrasEstoqueTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    [Fact]
    public void Custo_medio_e_ponderado_pelo_saldo_e_ignora_lotes_vazios()
    {
        // 100 US$ a 5,00 + 300 US$ a 5,40 → (500 + 1620) / 400 = 5,30
        var medio = RegrasEstoque.CustoMedio([new(A, 100, 5.0m), new(B, 300, 5.4m), new(C, 0, 9m)]);
        Assert.Equal(5.3m, medio);
        Assert.Equal(0m, RegrasEstoque.CustoMedio([]));
    }

    [Fact]
    public void Fifo_tira_do_lote_mais_antigo_primeiro()
    {
        var plano = RegrasEstoque.PlanoFifo([new(A, 10, 5m), new(B, 20, 6m)], 15, "US$");
        Assert.Equal([new Debito(A, 10, 5m), new Debito(B, 5, 6m)], plano);
    }

    [Fact]
    public void Sem_saldo_suficiente_nada_sai()
    {
        var e = Assert.Throws<DomainException>(() => RegrasEstoque.PlanoFifo([new(A, 10, 5m)], 10.5m, "US$"));
        Assert.Equal("saldo_insuficiente", e.Code);
        Assert.Throws<DomainException>(() => RegrasEstoque.PlanoProporcional([new(A, 10, 5m)], 0, "US$"));
    }

    [Fact]
    public void Proporcional_mantem_o_custo_medio_e_fecha_a_quantidade()
    {
        var lotes = new List<SaldoLote> { new(A, 100, 5.0m), new(B, 300, 5.4m), new(C, 33.33m, 5.1m) };
        var antes = RegrasEstoque.CustoMedio(lotes);
        var plano = RegrasEstoque.PlanoProporcional(lotes, 100, "US$");
        Assert.Equal(100m, plano.Sum(d => d.Quantidade));
        var depois = lotes.Select(l => l with { Saldo = l.Saldo - plano.Where(d => d.LoteId == l.Id).Sum(d => d.Quantidade) }).ToList();
        Assert.All(depois, l => Assert.True(l.Saldo >= 0));
        Assert.Equal(antes, RegrasEstoque.CustoMedio(depois), 4);
    }

    [Fact]
    public void Proporcional_pode_zerar_tudo()
    {
        var plano = RegrasEstoque.PlanoProporcional([new(A, 1.5m, 5m), new(B, 2.25m, 6m)], 3.75m, "US$");
        Assert.Equal(1.5m, plano.Where(d => d.LoteId == A).Sum(d => d.Quantidade));
        Assert.Equal(2.25m, plano.Where(d => d.LoteId == B).Sum(d => d.Quantidade));
    }
}

/// <summary>Mesmos números de src/frontend/src/__tests__/calculator.spec.ts: o servidor e a tela precisam bater.</summary>
public sealed class CalculoVendaTests
{
    private static readonly ConfigCalculo Cfg = ConfigCalculo.Padrao;
    private static readonly IReadOnlyList<FaixaCalculo> Faixas = CalculoPreco.FaixasPadrao;

    [Fact]
    public void Custo_de_10_dolares()
    {
        var r = CalculoVenda.DeUsd(10, Cfg, Faixas);
        Assert.Equal(57, r.CustoBrl);
        Assert.Equal(4385, r.CustoCambiosBase);
        Assert.Equal(4388, r.CustoCambios);
        Assert.Equal(5543, r.PrecoVendaCambios);
        Assert.Equal(6.2, r.PixMultiplicador);
        Assert.Equal(62, r.PrecoVendaPix);
    }

    [Fact]
    public void Custo_de_30_dolares_usa_o_piso()
    {
        var r = CalculoVenda.DeUsd(30, Cfg, Faixas);
        Assert.Equal(171, r.CustoBrl);
        Assert.Equal(180.04, r.PrecoVendaPix);
        Assert.Equal(16619, r.PrecoVendaCambios);
    }

    [Fact]
    public void Venda_pix_de_62_com_custo_de_10_dolares()
    {
        var s = CalculoVenda.Venda(10, 62, 0, Cfg, Faixas);
        Assert.Equal(57.04, s.CustoBrl);
        Assert.Equal(4.96, s.LucroBrl);
        Assert.Equal(8.69, s.MargemPercent);
        Assert.Equal(3, s.TaxaPrataAplicada);
    }

    [Fact]
    public void Custo_em_cambios_nao_passa_pelo_dolar()
    {
        var s = CalculoVenda.Venda(0, 0, 1500, Cfg, Faixas, custoCambiosManual: 1000);
        Assert.Equal(13, s.CustoBrl);
        Assert.Equal(19.5, s.ReceitaTotalBrl);
        Assert.Equal(6.5, s.LucroBrl);
        Assert.Equal(50, s.MargemPercent);
        Assert.Null(s.DolarCompraUsado);
    }

    [Fact]
    public void Dolar_do_estoque_substitui_o_padrao_de_compra()
    {
        var s = CalculoVenda.Venda(10, 0, 5000, Cfg with { TaxaPrata = 0 }, Faixas, dolarCompra: 5.2);
        Assert.Equal(52, s.CustoBrl);
        Assert.Equal(5.2, s.DolarCompraUsado);
        Assert.Equal(13, s.LucroBrl);
    }
}

public sealed class CustoVendaSiteTests
{
    [Fact]
    public void Custo_e_o_dolar_dos_itens_vezes_o_custo_medio_do_estoque()
    {
        // US$ 10 × R$ 5,30 = R$ 53,00; pago R$ 80,00 → lucro R$ 27,00 (33,75%)
        var (custo, lucro, margem) = CustoVendaSite.Calcular(80m, 10m, 5.3m);
        Assert.Equal(53m, custo);
        Assert.Equal(27m, lucro);
        Assert.Equal(33.75m, margem);
    }

    [Fact]
    public void Custo_conhecido_dos_lotes_define_lucro_e_margem()
    {
        // lotes que saíram custaram R$ 1.081,34; pago R$ 1.190,00 → lucro R$ 108,66 (9,13%)
        var (custo, lucro, margem) = CustoVendaSite.ComCusto(1190m, 1081.34m);
        Assert.Equal(1081.34m, custo);
        Assert.Equal(108.66m, lucro);
        Assert.Equal(9.13m, margem);
    }

    [Fact]
    public void Venda_sem_valor_pago_tem_margem_zero()
    {
        var (_, lucro, margem) = CustoVendaSite.Calcular(0m, 10m, 5m);
        Assert.Equal(-50m, lucro);
        Assert.Equal(0m, margem);
    }

    [Fact]
    public void Vendas_anteriores_ao_inicio_mantem_o_custo_do_crivonft()
    {
        Assert.False(CustoVendaSite.Vale(new DateTime(2026, 10, 25, 23, 59, 0, DateTimeKind.Utc)));
        Assert.True(CustoVendaSite.Vale(new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc)));
    }
}

public sealed class PlanoFifoAteDataTests
{
    [Fact]
    public void Usa_primeiro_os_lotes_ja_comprados_no_dia_da_venda_e_so_depois_os_futuros()
    {
        var a = new LoteSimulado(Guid.NewGuid(), new DateOnly(2026, 10, 1), 10m, 5m);
        var b = new LoteSimulado(Guid.NewGuid(), new DateOnly(2026, 10, 10), 10m, 6m);
        var (plano, faltou) = RegrasEstoque.PlanoFifoAteData([a, b], 15m, new DateOnly(2026, 10, 5));
        Assert.Equal(0m, plano.Single(d => d.LoteId == a.Id).Quantidade - 10m);
        Assert.Equal(5m, plano.Single(d => d.LoteId == b.Id).Quantidade); // o lote A não bastou: completa com o futuro
        Assert.Equal(0m, faltou);
        Assert.Equal(0m, a.Saldo);
        Assert.Equal(5m, b.Saldo);
    }

    [Fact]
    public void Falta_de_saldo_devolve_o_que_faltou_sem_erro()
    {
        var a = new LoteSimulado(Guid.NewGuid(), new DateOnly(2026, 10, 1), 4m, 5m);
        var (plano, faltou) = RegrasEstoque.PlanoFifoAteData([a], 10m, new DateOnly(2026, 10, 2));
        Assert.Single(plano);
        Assert.Equal(6m, faltou);
    }
}
