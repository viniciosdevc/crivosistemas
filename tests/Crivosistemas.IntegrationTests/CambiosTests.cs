using System.Net;
using System.Text.Json;

namespace Crivosistemas.IntegrationTests;

/// <summary>O saldo de câmbios é único no banco compartilhado: um cenário só, comparando com o saldo de antes.</summary>
[Collection("api")]
public sealed class CambiosTests(ApiFactory factory)
{
    private static decimal D(JsonElement e, string p) => e.GetProperty(p).GetDecimal();

    [Fact]
    public async Task Saldo_cresce_com_venda_manual_em_cambios_e_cai_na_venda_de_cambios()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        (await admin.Post("/api/cambios/saldo", new { saldo = 1000, motivo = "inicial" })).EnsureSuccessStatusCode();
        Assert.Equal(1000m, D(await (await admin.GetAsync("/api/cambios/resumo")).Json(), "saldo"));

        // Venda manual paga em câmbios: só gera saldo quando entregue; cancelar desfaz.
        var nick = "cli-" + Guid.NewGuid().ToString("N")[..6];
        var venda = await (await admin.Post("/api/vendas/manuais", new
        {
            nicknameComprador = nick, nomeItem = "Item", formaPagamento = "cambios", origemCusto = "manual", custoUsd = 0, custoCambios = 0,
            quantidadeEsmeraldas = 0, usaTaxaPrata = false, valorVendaPix = 0, valorVendaCambios = 300,
        })).Json();
        Assert.Equal(1000m, D(await (await admin.GetAsync("/api/cambios/resumo")).Json(), "saldo"));
        var vid = venda.GetProperty("id").GetGuid();
        (await admin.Post($"/api/vendas/{vid}/entregar", new { })).EnsureSuccessStatusCode();
        Assert.Equal(1300m, D(await (await admin.GetAsync("/api/cambios/resumo")).Json(), "saldo"));
        (await admin.Post($"/api/vendas/{vid}/cancelar", new { motivo = "teste" })).EnsureSuccessStatusCode();
        Assert.Equal(1000m, D(await (await admin.GetAsync("/api/cambios/resumo")).Json(), "saldo"));

        // Venda de câmbios: debita, guarda saldo anterior e novo, liga ao cliente.
        var rv = await admin.Post("/api/cambios/vendas", new { quantidade = 400, valorBrl = 80, nickname = nick });
        Assert.Equal(HttpStatusCode.Created, rv.StatusCode);
        var v = await rv.Json();
        Assert.Equal(1000m, D(v, "saldoAnterior"));
        Assert.Equal(600m, D(v, "saldoNovo"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Post("/api/cambios/vendas", new { quantidade = 700, valorBrl = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/cambios/vendas", new { quantidade = 0, valorBrl = 10 })).StatusCode);

        var clientes = await (await admin.GetAsync($"/api/clientes?busca={nick}")).Json();
        var cid = clientes.GetProperty("items")[0].GetProperty("id").GetGuid();
        var movs = await (await admin.GetAsync($"/api/cambios/movimentos?clienteId={cid}")).Json();
        Assert.Equal(3, movs.GetProperty("total").GetInt32()); // crédito da venda manual, seu estorno e a venda de câmbios
        Assert.Contains(movs.GetProperty("items").EnumerateArray(), m => m.GetProperty("tipo").GetString() == "venda" && D(m, "valorBrl") == 80m);

        // Estorno devolve o saldo e só vale uma vez.
        var id = v.GetProperty("id").GetGuid();
        var est = await admin.Post($"/api/cambios/movimentos/{id}/estornar", new { motivo = "erro" });
        Assert.Equal(1000m, D(await est.Json(), "saldoNovo"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Post($"/api/cambios/movimentos/{id}/estornar", new { })).StatusCode);
    }
}
