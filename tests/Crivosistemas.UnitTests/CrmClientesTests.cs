using Crivosistemas.Domain.Vendas;

namespace Crivosistemas.UnitTests;

public sealed class CrmClientesTests
{
    private static readonly DateTime Agora = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static ClienteCompras C(string nick, decimal total, int n, int primeiraHa, int ultimaHa, bool fav = false) =>
        new(Guid.NewGuid(), nick, fav, total, n, n == 0 ? null : Agora.AddDays(-primeiraHa), n == 0 ? null : Agora.AddDays(-ultimaHa));

    [Fact]
    public void Segmenta_e_calcula_totais()
    {
        var lista = new List<ClienteCompras>
        {
            C("vip", 1000, 5, 200, 5),
            C("recorrente", 200, 3, 150, 20),
            C("novo", 50, 1, 5, 5),
            C("ocasional", 40, 1, 90, 40),
            C("risco", 100, 2, 300, 90),
            C("inativo", 300, 2, 400, 200, fav: true),
            C("nunca", 0, 0, 0, 0),
        };
        // 6 compradores -> VIP = o maior gastador (10% arredondado para cima = 1).
        var a = CrmClientes.Analisar(lista, Agora, 60);
        Seg(a, "vip", SegmentoCliente.Vip);
        Seg(a, "recorrente", SegmentoCliente.Recorrente);
        Seg(a, "novo", SegmentoCliente.Novo);
        Seg(a, "ocasional", SegmentoCliente.Ocasional);
        Seg(a, "risco", SegmentoCliente.EmRisco);
        Seg(a, "inativo", SegmentoCliente.Inativo);
        Seg(a, "nunca", SegmentoCliente.NuncaComprou);

        Assert.Equal(7, a.Totais.Clientes);
        Assert.Equal(6, a.Totais.Compradores);
        Assert.Equal(1, a.Totais.NuncaComprou);
        Assert.Equal(1690m, a.Totais.Receita);
        Assert.Equal(1690m / 14, a.Totais.TicketMedio);
        Assert.Equal(4m, Math.Round(a.Totais.TaxaRecompra / 100m * a.Totais.Compradores));
        Assert.Equal(new[] { "inativo", "risco" }, a.Reativar.Select(r => r.Nickname)); // favorito primeiro
        Assert.Equal("vip", a.Top[0].Nickname);
    }

    private static void Seg(AnaliseCrm a, string nick, SegmentoCliente esperado)
    {
        var todos = a.Top.Concat(a.Reativar);
        if (esperado == SegmentoCliente.NuncaComprou) { Assert.Equal(1, a.Segmentos.Single(s => s.Segmento == esperado).Clientes); return; }
        Assert.Contains(todos, r => r.Nickname == nick && r.Segmento == esperado);
    }
}

