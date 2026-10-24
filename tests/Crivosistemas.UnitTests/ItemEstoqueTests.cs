using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Estoque;

namespace Crivosistemas.UnitTests;

public sealed class ItemEstoqueTests
{
    private static ItemEstoque Item() => new() { Nome = "Dragão", Quantidade = 2, CustoUnitario = 10m, DataCompra = new DateOnly(2026, 10, 1) };

    [Fact]
    public void Custo_total_multiplica_pela_quantidade() => Assert.Equal(20m, Item().CustoTotal);

    [Fact]
    public void Vender_fecha_o_item_e_so_uma_vez()
    {
        var i = Item();
        i.Vender(35m, new DateOnly(2026, 10, 5));
        Assert.Equal(StatusItem.Vendido, i.Status);
        Assert.Equal(35m, i.ValorVenda);
        Assert.Throws<DomainException>(() => i.Vender(1m, new DateOnly(2026, 10, 6)));
        Assert.Throws<DomainException>(() => i.Cancelar("erro"));
    }

    [Fact]
    public void Cancelar_so_vale_em_estoque()
    {
        var i = Item();
        i.Cancelar("  lançado errado ");
        Assert.Equal(StatusItem.Cancelado, i.Status);
        Assert.Equal("lançado errado", i.MotivoCancelamento);
        Assert.Throws<DomainException>(() => i.Vender(1m, new DateOnly(2026, 10, 6)));
    }
}
