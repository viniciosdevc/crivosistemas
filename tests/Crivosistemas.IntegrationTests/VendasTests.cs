using System.Net;
using System.Text.Json;

namespace Crivosistemas.IntegrationTests;

[Collection("api")]
public sealed class VendasTests(ApiFactory factory)
{
    // Período isolado (2019-03) para que o resumo não some vendas de outros testes que compartilham o banco.
    private static readonly DateTime D1 = new(2019, 3, 10, 15, 0, 0, DateTimeKind.Utc);

    private async Task<HttpClient> Semear(string tag)
    {
        var m = factory.Maquina();
        async Task S(string id, DateTime t, decimal total, string st, string origem, string? nick, string? forma, decimal lucro = 10, string desc = "NFT")
            => Assert.Equal(HttpStatusCode.OK, (await m.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda($"{tag}-{id}", t, total, st, origem, nick, forma, lucro: lucro, descricao: desc)))).Status);
        await S("a", D1, 100m, "concluido", "nft", $"{tag}-ana", "pix", 20, "Dragão Dourado");
        await S("b", D1.AddHours(2), 50m, "pago", "cambios", $"{tag}-ana", "cambios", 5);
        await S("c", D1.AddDays(1), 200m, "concluido", "site", $"{tag}-bia", "hibrido", 40);
        await S("d", D1.AddDays(1), 999m, "cancelado", "nft", $"{tag}-bia", "pix", 99);
        await S("e", D1.AddDays(1), 30m, "pendente", "site", $"{tag}-bia", "pix", 3);
        // 02:00 UTC do dia 12 ainda é dia 11 em São Paulo (UTC-3)
        await S("f", new DateTime(2019, 3, 12, 2, 0, 0, DateTimeKind.Utc), 70m, "concluido", "nft", $"{tag}-ana", "pix", 7);
        await S("g", D1.AddDays(2), 500m, "concluido", "nft", $"{tag}-bia", "pix", 50);
        await m.Enviar(CrivoHelpers.Evento("venda.removida", new { origem = "nft", origem_id = $"{tag}-g" }));
        return await factory.SeedAndLoginAdminAsync();
    }

    [Fact]
    public async Task Listagem_filtra_por_periodo_origem_status_forma_cliente_e_busca()
    {
        var tag = "v" + Guid.NewGuid().ToString("N")[..8];
        var c = await Semear(tag);

        var todas = await (await c.GetAsync($"/api/vendas?busca={tag}&pageSize=50")).Json();
        Assert.Equal(6, todas.GetProperty("total").GetInt32()); // a venda removida fica de fora

        async Task<int> Total(string qs) => (await (await c.GetAsync($"/api/vendas?busca={tag}&{qs}")).Json()).GetProperty("total").GetInt32();
        Assert.Equal(2, await Total("origem=site"));
        Assert.Equal(3, await Total("statusNormalizado=concluido"));
        Assert.Equal(1, await Total("statusNormalizado=cancelado"));
        Assert.Equal(1, await Total("formaPagamento=cambios"));
        Assert.Equal(1, await Total("formaPagamento=hibrido"));
        Assert.Equal(7, await Total("incluirRemovidas=true"));
        // Dias de São Paulo: 10 tem a e b; 11 tem c, d, e e f (02:00 UTC do dia 12 ainda é 23:00 do dia 11); 12 tem g (removida, fora).
        Assert.Equal(2, await Total("de=2019-03-10&ate=2019-03-10"));
        Assert.Equal(4, await Total("de=2019-03-11&ate=2019-03-11"));
        Assert.Equal(6, await Total("de=2019-03-10&ate=2019-03-12"));
        Assert.Equal(2, await Total("de=2019-03-11&ate=2019-03-11&origem=nft"));
        Assert.Equal(1, await Total("de=2019-03-11&ate=2019-03-11&origem=nft&statusNormalizado=concluido&formaPagamento=pix"));

        var porCliente = await (await c.GetAsync($"/api/clientes?busca={tag}-ana")).Json();
        var cid = porCliente.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(3, await Total($"clienteId={cid}"));
        // Busca pela descrição do item
        var porItem = await (await c.GetAsync("/api/vendas?busca=Drag%C3%A3o%20Dourado")).Json();
        Assert.True(porItem.GetProperty("total").GetInt32() >= 1);
        Assert.Contains(porItem.GetProperty("items").EnumerateArray(), v => v.GetProperty("origemId").GetString() == $"{tag}-a");
    }

    [Fact]
    public async Task Filtros_invalidos_respondem_400_e_a_busca_exige_login()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/vendas")).StatusCode);
        var c = await factory.SeedAndLoginAdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/vendas?origem=xpto")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/vendas?de=2026-02-02&ate=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/vendas/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Operador_tambem_le_vendas_e_clientes()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var op = await factory.CreateAndLoginAsync(admin, "operator");
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync("/api/vendas")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync("/api/vendas/resumo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync("/api/clientes")).StatusCode);
    }

    [Fact]
    public async Task Resumo_soma_so_vendas_faturaveis_agrupa_por_origem_e_por_dia_em_sao_paulo()
    {
        var tag = "r" + Guid.NewGuid().ToString("N")[..8];
        // Período exclusivo deste teste: 2018-07
        var t = new DateTime(2018, 7, 10, 15, 0, 0, DateTimeKind.Utc);
        var m = factory.Maquina();
        async Task S(string id, DateTime when, decimal total, string st, string origem, decimal lucro)
            => await m.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda($"{tag}-{id}", when, total, st, origem, "resumo-cli", "pix", lucro: lucro)));
        await S("a", t, 100m, "concluido", "nft", 20);
        await S("b", t.AddHours(1), 50m, "pago", "cambios", 5);
        await S("c", t.AddDays(1), 200m, "concluido", "site", 40);
        await S("d", t.AddDays(1), 999m, "cancelado", "nft", 99);
        await S("e", t.AddDays(1), 30m, "pendente", "site", 3);
        await S("f", t.AddDays(2), 500m, "concluido", "nft", 50);
        await m.Enviar(CrivoHelpers.Evento("venda.removida", new { origem = "nft", origem_id = $"{tag}-f" }));
        await S("g", new DateTime(2018, 7, 13, 2, 0, 0, DateTimeKind.Utc), 70m, "concluido", "nft", 7); // 23:00 do dia 12 em SP

        var c = await factory.SeedAndLoginAdminAsync();
        var r = await (await c.GetAsync("/api/vendas/resumo?de=2018-07-10&ate=2018-07-13")).Json();
        var totais = r.GetProperty("totais");
        Assert.Equal(420m, totais.GetProperty("faturamento").GetDecimal()); // 100 + 50 + 200 + 70
        Assert.Equal(72m, totais.GetProperty("lucro").GetDecimal());
        Assert.Equal(4, totais.GetProperty("quantidade").GetInt32());
        Assert.Equal(105m, totais.GetProperty("ticketMedio").GetDecimal());

        decimal Orig(string o) => r.GetProperty("porOrigem").EnumerateArray().First(x => x.GetProperty("origem").GetString() == o).GetProperty("faturamento").GetDecimal();
        Assert.Equal(170m, Orig("nft"));
        Assert.Equal(50m, Orig("cambios"));
        Assert.Equal(200m, Orig("site"));

        var dias = r.GetProperty("porDia").EnumerateArray().ToDictionary(x => x.GetProperty("dia").GetString()!, x => x.GetProperty("faturamento").GetDecimal());
        Assert.Equal(4, dias.Count);
        Assert.Equal(150m, dias["2018-07-10"]);
        Assert.Equal(200m, dias["2018-07-11"]);
        Assert.Equal(70m, dias["2018-07-12"]); // a venda de 02:00 UTC do dia 13 cai no dia 12 de SP
        Assert.Equal(0m, dias["2018-07-13"]);
    }

    [Fact]
    public async Task Clientes_listam_total_gasto_compras_e_ultima_compra_e_detalham_o_historico()
    {
        var tag = "c" + Guid.NewGuid().ToString("N")[..8];
        var m = factory.Maquina();
        var nick = $"{tag}-comprador";
        await m.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda($"{tag}-1", new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc), 100m, nick: nick)));
        await m.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda($"{tag}-2", new DateTime(2020, 6, 1, 12, 0, 0, DateTimeKind.Utc), 40m, "pago", nick: nick)));
        await m.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda($"{tag}-3", new DateTime(2020, 7, 1, 12, 0, 0, DateTimeKind.Utc), 900m, "cancelado", nick: nick)));
        await m.Enviar(CrivoHelpers.Evento("cliente.upsert", new { chave = $"{tag}-sem-compras", nickname = $"{tag}-sem-compras" }));

        var c = await factory.SeedAndLoginAdminAsync();
        var lista = await (await c.GetAsync($"/api/clientes?busca={tag}&ordem=totalGasto")).Json();
        Assert.Equal(2, lista.GetProperty("total").GetInt32());
        var a = lista.GetProperty("items")[0];
        Assert.Equal(nick, a.GetProperty("nickname").GetString());
        Assert.Equal(140m, a.GetProperty("totalGasto").GetDecimal());
        Assert.Equal(2, a.GetProperty("compras").GetInt32());
        Assert.StartsWith("2020-06-01", a.GetProperty("ultimaCompra").GetString());
        Assert.Equal(0m, lista.GetProperty("items")[1].GetProperty("totalGasto").GetDecimal());

        var det = await (await c.GetAsync($"/api/clientes/{a.GetProperty("id").GetGuid()}")).Json();
        Assert.Equal(3, det.GetProperty("vendas").GetArrayLength());
        Assert.Equal(140m, det.GetProperty("cliente").GetProperty("totalGasto").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/clientes/{Guid.NewGuid()}")).StatusCode);
    }
}
