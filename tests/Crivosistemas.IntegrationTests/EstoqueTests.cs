using System.Net;
using System.Text.Json;

namespace Crivosistemas.IntegrationTests;

/// <summary>O estoque é único no banco compartilhado: os cenários rodam em sequência num teste só, comparando com o saldo de antes.</summary>
[Collection("api")]
public sealed class EstoqueTests(ApiFactory factory)
{
    private static decimal D(JsonElement e, string p) => e.GetProperty(p).GetDecimal();

    private static async Task<JsonElement> Resumo(HttpClient c, string tipo) =>
        (await (await c.GetAsync("/api/estoque/resumo")).Json()).GetProperty(tipo);

    [Fact]
    public async Task Compras_custo_medio_ajustes_venda_manual_entrega_e_estorno()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var op = await factory.CreateAndLoginAsync(admin, "operator");

        // Zera os dois estoques para os números serem exatos.
        foreach (var t in new[] { "usd", "esmeraldas" })
        {
            var s = D(await Resumo(admin, t == "usd" ? "usd" : "esmeraldas"), "saldo");
            if (s > 0) (await admin.Post($"/api/estoque/{t}/saldo", new { saldo = 0, motivo = "teste" })).EnsureSuccessStatusCode();
        }
        (await admin.Put("/api/estoque/usd/custo-medio-fixo", new { valor = (decimal?)null })).EnsureSuccessStatusCode();

        // Operador registra compras de dólar: 100 US$ por R$ 500 e 300 US$ por R$ 1.620 → custo médio 5,30.
        var r1 = await op.Post("/api/estoque/usd/compras", new { data = "2026-10-01", quantidade = 100, custoTotal = 500, observacao = "Binance" });
        if (r1.StatusCode != HttpStatusCode.Created) Assert.Fail(await r1.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        var lote1 = await r1.Json();
        Assert.Equal(5m, D(lote1, "custoUnitario"));
        var lote2 = await (await op.Post("/api/estoque/usd/compras", new { data = "2026-10-05", quantidade = 300, custoTotal = 1620 })).Json();
        var usd = await Resumo(op, "usd");
        Assert.Equal(400m, D(usd, "saldo"));
        Assert.Equal(5.3m, D(usd, "custoMedio"));

        // Validação e permissões
        Assert.Equal(HttpStatusCode.BadRequest, (await op.Post("/api/estoque/usd/compras", new { data = "2026-10-01", quantidade = 0, custoTotal = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await op.Post("/api/estoque/ouro/compras", new { data = "2026-10-01", quantidade = 1, custoTotal = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await op.Put("/api/estoque/usd/custo-medio-fixo", new { valor = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/estoque/resumo")).StatusCode);

        // Editar uma compra intocada recalcula o custo unitário
        var editado = await (await op.Put($"/api/estoque/usd/compras/{lote2.GetProperty("id").GetGuid()}", new { data = "2026-10-05", quantidade = 300, custoTotal = 1650 })).Json();
        Assert.Equal(5.5m, D(editado, "custoUnitario"));
        Assert.Equal(5.375m, D(await Resumo(op, "usd"), "custoMedio")); // (500 + 1650) / 400

        // Custo médio fixo pelo admin vence o calculado; null volta ao cálculo
        Assert.Equal(5.1m, D(await (await admin.Put("/api/estoque/usd/custo-medio-fixo", new { valor = 5.1m })).Json(), "custoMedio"));
        Assert.Equal(5.375m, D(await (await admin.Put("/api/estoque/usd/custo-medio-fixo", new { valor = (decimal?)null })).Json(), "custoMedio"));

        // Esmeraldas: 1.000 por US$ 9,44
        (await op.Post("/api/estoque/esmeraldas/compras", new { data = "2026-10-02", quantidade = 1000, custoTotal = 9.44m })).EnsureSuccessStatusCode();
        Assert.Equal(0.00944m, D(await Resumo(op, "esmeraldas"), "custoMedio"));

        // Venda manual com dólar do estoque, pendente: custo pelo custo médio, sem mexer no saldo ainda.
        var nick = "cli-" + Guid.NewGuid().ToString("N")[..6];
        var rv = await op.Post("/api/vendas/manuais", new
        {
            nicknameComprador = nick, nomeItem = "Trono Dourado", formaPagamento = "pix", origemCusto = "estoque_usd", custoUsd = 150, custoCambios = 0,
            quantidadeEsmeraldas = 0, usaTaxaPrata = false, valorVendaPix = 1000, valorVendaCambios = 0,
        });
        Assert.Equal(HttpStatusCode.Created, rv.StatusCode);
        var venda = await rv.Json();
        var vid = venda.GetProperty("id").GetGuid();
        Assert.Equal("manual", venda.GetProperty("origem").GetString());
        Assert.Equal("pendente", venda.GetProperty("statusNormalizado").GetString());
        Assert.Equal(nick, venda.GetProperty("cliente").GetProperty("nickname").GetString());
        Assert.Equal(1000m, D(venda, "totalBrl"));
        Assert.Equal(1000m - 150m * 5.375m, D(venda, "lucroBrl")); // custo = 150 × 5,375 = 806,25 (sem taxa de prata)
        Assert.Equal(400m, D(await Resumo(op, "usd"), "saldo"));

        // Entregar debita por FIFO: 100 do lote de 01/10 e 50 do de 05/10.
        (await op.Post($"/api/vendas/{vid}/entregar", new { })).EnsureSuccessStatusCode();
        Assert.Equal(250m, D(await Resumo(op, "usd"), "saldo"));
        var lotes = (await (await op.GetAsync("/api/estoque/usd/lotes?comSaldo=true")).Json()).GetProperty("items");
        Assert.Equal(1, lotes.GetArrayLength());
        Assert.Equal(250m, D(lotes[0], "saldo"));
        Assert.Equal(5.5m, D(await Resumo(op, "usd"), "custoMedio"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await op.Post($"/api/vendas/{vid}/entregar", new { })).StatusCode);

        // Lote consumido não pode ser editado nem cancelado
        var c1 = await op.Post($"/api/estoque/usd/compras/{lote1.GetProperty("id").GetGuid()}/cancelar", new { motivo = "errado" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, c1.StatusCode);

        var detalhe = await (await op.GetAsync($"/api/vendas/{vid}")).Json();
        Assert.Equal("estoque_usd", detalhe.GetProperty("manual").GetProperty("origemCusto").GetString());
        Assert.Equal(2, detalhe.GetProperty("manual").GetProperty("movimentos").GetArrayLength());
        Assert.True(detalhe.GetProperty("venda").GetProperty("faturavel").GetBoolean());
        Assert.Equal("Trono Dourado", detalhe.GetProperty("venda").GetProperty("item").GetString());
        var lista = (await (await op.GetAsync($"/api/vendas?busca={nick}")).Json()).GetProperty("items")[0];
        Assert.Equal("Trono Dourado", lista.GetProperty("item").GetString());

        // Imagem do item vem do crivonft, com cache: a segunda chamada não consulta de novo.
        factory.Crivonft.Imagens["Trono Dourado"] = "https://img.example/trono.png";
        var antes = factory.Crivonft.ConsultasImagens;
        var img = (await (await op.GetAsync("/api/vendas/imagens?nome=Trono%20Dourado&nome=Sem%20Foto")).Json()).GetProperty("imagens");
        Assert.Equal("https://img.example/trono.png", img.GetProperty("Trono Dourado").GetString());
        Assert.Equal(JsonValueKind.Null, img.GetProperty("Sem Foto").ValueKind);
        await op.GetAsync("/api/vendas/imagens?nome=Trono%20Dourado");
        Assert.Equal(antes + 1, factory.Crivonft.ConsultasImagens);

        // Cancelar devolve aos mesmos lotes: custo médio volta a 5,375.
        (await op.Post($"/api/vendas/{vid}/cancelar", new { motivo = "desistiu" })).EnsureSuccessStatusCode();
        usd = await Resumo(op, "usd");
        Assert.Equal(400m, D(usd, "saldo"));
        Assert.Equal(5.375m, D(usd, "custoMedio"));

        // Venda com esmeraldas: sai na criação; sem saldo, 422 e nada muda.
        var semSaldo = await op.Post("/api/vendas/manuais", new
        {
            nomeItem = "Item", formaPagamento = "cambios", origemCusto = "estoque_esmeralda", custoUsd = 0, custoCambios = 0, quantidadeEsmeraldas = 5000,
            usaTaxaPrata = true, valorVendaPix = 0, valorVendaCambios = 900,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semSaldo.StatusCode);
        var ve = await (await op.Post("/api/vendas/manuais", new
        {
            nomeItem = "Item", formaPagamento = "cambios", origemCusto = "estoque_esmeralda", custoUsd = 0, custoCambios = 0, quantidadeEsmeraldas = 300,
            usaTaxaPrata = true, valorVendaPix = 0, valorVendaCambios = 900, entregue = true,
        })).Json();
        Assert.Equal("concluido", ve.GetProperty("statusNormalizado").GetString());
        Assert.Equal(700m, D(await Resumo(op, "esmeraldas"), "saldo"));
        Assert.Equal(400m, D(await Resumo(op, "usd"), "saldo")); // esmeralda não mexe no dólar

        // Ajustes: saída proporcional mantém o custo médio; definir saldo lança a diferença.
        (await op.Post("/api/estoque/usd/ajustes", new { direcao = "saida", quantidade = 40, motivo = "taxa" })).EnsureSuccessStatusCode();
        usd = await Resumo(op, "usd");
        Assert.Equal(360m, D(usd, "saldo"));
        Assert.Equal(5.375m, Math.Round(D(usd, "custoMedio"), 4));
        Assert.Equal(500m, D(await (await op.Post("/api/estoque/usd/saldo", new { saldo = 500, motivo = "contagem" })).Json(), "saldo"));

        // Venda do crivonft não pode ser alterada por aqui
        var m = factory.Maquina();
        var origemId = "nft-" + Guid.NewGuid().ToString("N")[..8];
        await m.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(origemId, DateTime.UtcNow, 10m)));
        var externa = (await (await admin.GetAsync($"/api/vendas?busca={origemId}")).Json()).GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Post($"/api/vendas/{externa}/cancelar", new { })).StatusCode);

        var movs = await (await op.GetAsync("/api/estoque/usd/movimentos?pageSize=100")).Json();
        Assert.True(movs.GetProperty("total").GetInt32() >= 8);
    }

    [Fact]
    public async Task Importa_lotes_do_crivonft_uma_vez()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var id = Guid.NewGuid().ToString();
        factory.Crivonft.EstoqueUsd.Clear();
        factory.Crivonft.EstoqueUsd.Add(FakeCrivonftClient.Linha($$"""
            {"id":"{{id}}","data_compra":"2026-09-30T00:00:00.000Z","valor_brl_depositado":"540.0000","valor_usd_recebido":"100.0000","saldo_usd":"60.0000","custo_unitario_dolar":"5.40000000"}
            """));
        factory.Crivonft.EstoqueUsd.Add(FakeCrivonftClient.Linha("""{"id":"vazio","data_compra":"2026-09-01","valor_brl_depositado":"1","valor_usd_recebido":"1","saldo_usd":"0"}"""));
        var r = await (await admin.Post("/api/estoque/importar-crivonft", new { })).Json();
        Assert.Equal(1, r.GetProperty("usdImportados").GetInt32());
        var r2 = await (await admin.Post("/api/estoque/importar-crivonft", new { })).Json();
        Assert.Equal(0, r2.GetProperty("usdImportados").GetInt32());

        var lotes = (await (await admin.GetAsync("/api/estoque/usd/lotes?pageSize=100")).Json()).GetProperty("items").EnumerateArray()
            .Where(l => l.GetProperty("origem").GetString() == "importacao_crivonft" && l.GetProperty("dataCompra").GetString() == "2026-09-30").ToList();
        Assert.Contains(lotes, l => D(l, "saldo") == 60m && D(l, "custoUnitario") == 5.4m && !l.GetProperty("editavel").GetBoolean());
        factory.Crivonft.EstoqueUsd.Clear();
    }
}
