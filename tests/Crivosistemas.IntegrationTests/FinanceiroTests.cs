using System.Net;
using System.Text.Json;

namespace Crivosistemas.IntegrationTests;

/// <summary>O livro-razão é único no banco compartilhado: os cenários comparam com o saldo de antes.</summary>
[Collection("api")]
public sealed class FinanceiroTests(ApiFactory factory)
{
    private static decimal D(JsonElement e, string p) => e.GetProperty(p).GetDecimal();

    private static async Task<decimal> Saldo(HttpClient c) => D(await (await c.GetAsync("/api/financeiro/resumo")).Json(), "saldo");

    private static async Task<JsonElement> Lancamentos(HttpClient c, string busca) =>
        (await (await c.GetAsync($"/api/financeiro/lancamentos?busca={Uri.EscapeDataString(busca)}")).Json()).GetProperty("items");

    [Fact]
    public async Task Lancamentos_manuais_confirmar_cancelar_e_permissoes()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var op = await factory.CreateAndLoginAsync(admin, "operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await op.GetAsync("/api/financeiro/resumo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/financeiro/resumo")).StatusCode);

        var antes = await Saldo(admin);
        var tag = "t" + Guid.NewGuid().ToString("N")[..6];
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/financeiro/lancamentos", new { tipo = "x", categoria = "Aporte", valorBrl = 10, data = hoje })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/financeiro/lancamentos", new { tipo = "entrada", categoria = "Aporte", valorBrl = 0, data = hoje })).StatusCode);

        var entrada = await (await admin.Post("/api/financeiro/lancamentos", new { tipo = "entrada", categoria = "Aporte", descricao = tag, valorBrl = 500, data = hoje })).Json();
        Assert.Equal("confirmado", entrada.GetProperty("status").GetString());
        var saida = await (await admin.Post("/api/financeiro/lancamentos", new { tipo = "saida", categoria = "Anúncios", descricao = tag, valorBrl = 120.50m, data = hoje })).Json();
        Assert.Equal(antes + 379.5m, await Saldo(admin));

        // Pendente não entra no saldo até confirmar.
        var pend = await (await admin.Post("/api/financeiro/lancamentos", new { tipo = "entrada", categoria = "Aporte", descricao = tag, valorBrl = 100, data = hoje, pendente = true })).Json();
        Assert.Equal(antes + 379.5m, await Saldo(admin));
        var pid = pend.GetProperty("id").GetGuid();
        (await admin.Post($"/api/financeiro/lancamentos/{pid}/confirmar", new { })).EnsureSuccessStatusCode();
        Assert.Equal(antes + 479.5m, await Saldo(admin));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Post($"/api/financeiro/lancamentos/{pid}/confirmar", new { })).StatusCode);

        // Cancelar exige motivo e tira do saldo, mas fica no histórico.
        var sid = saida.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Post($"/api/financeiro/lancamentos/{sid}/cancelar", new { motivo = " " })).StatusCode);
        (await admin.Post($"/api/financeiro/lancamentos/{sid}/cancelar", new { motivo = "digitado errado" })).EnsureSuccessStatusCode();
        Assert.Equal(antes + 600m, await Saldo(admin));
        Assert.Equal(3, (await Lancamentos(admin, tag)).GetArrayLength());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Post($"/api/financeiro/lancamentos/{sid}/cancelar", new { motivo = "de novo" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post($"/api/financeiro/lancamentos/{Guid.NewGuid()}/cancelar", new { motivo = "x" })).StatusCode);

        var cats = await (await admin.GetAsync("/api/financeiro/categorias")).Json();
        Assert.Contains("Anúncios", cats.EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Pix_das_vendas_e_compra_de_dolar_entram_sozinhos()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var antes = await Saldo(admin);

        // Venda do crivonft concluída com PIX entra; atualizar o valor reajusta; cancelar tira.
        var origemId = "fin-" + Guid.NewGuid().ToString("N")[..8];
        var m = factory.Maquina();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        await m.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(origemId, t0, 300m, descricao: "Mobi raro", nick: "comprador-fin", atualizadoEm: t0)));
        Assert.Equal(antes + 300m, await Saldo(admin));
        var l = (await Lancamentos(admin, "Mobi raro"))[0];
        Assert.Equal("venda", l.GetProperty("origem").GetString());
        Assert.True(l.GetProperty("automatico").GetBoolean());
        Assert.Contains("comprador-fin", l.GetProperty("descricao").GetString());

        await m.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(origemId, t0, 350m, descricao: "Mobi raro", nick: "comprador-fin", atualizadoEm: t0.AddMinutes(1))));
        Assert.Equal(antes + 350m, await Saldo(admin));

        await m.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(origemId, t0, 350m, statusNormalizado: "cancelado", descricao: "Mobi raro", nick: "comprador-fin", atualizadoEm: t0.AddMinutes(2))));
        Assert.Equal(antes, await Saldo(admin));

        // Lançamento automático não é mexido à mão.
        var lid = l.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Post($"/api/financeiro/lancamentos/{lid}/cancelar", new { motivo = "x" })).StatusCode);

        // Venda manual: pendente não entra; entregar faz entrar; cancelar tira.
        var vm = await (await admin.Post("/api/vendas/manuais", new
        {
            nicknameComprador = "cli-fin", nomeItem = "Dragão Fin", formaPagamento = "pix", origemCusto = "manual", custoUsd = 0, custoCambios = 0,
            quantidadeEsmeraldas = 0, usaTaxaPrata = false, valorVendaPix = 200, valorVendaCambios = 0,
        })).Json();
        var vid = vm.GetProperty("id").GetGuid();
        Assert.Equal(antes, await Saldo(admin));
        (await admin.Post($"/api/vendas/{vid}/entregar", new { })).EnsureSuccessStatusCode();
        Assert.Equal(antes + 200m, await Saldo(admin));
        (await admin.Post($"/api/vendas/{vid}/cancelar", new { motivo = "desistiu" })).EnsureSuccessStatusCode();
        Assert.Equal(antes, await Saldo(admin));

        // Compra de dólar sai do caixa; editar reajusta; cancelar devolve.
        var lote = await (await admin.Post("/api/estoque/usd/compras", new { data = "2026-10-20", quantidade = 100, custoTotal = 530 })).Json();
        var loteId = lote.GetProperty("id").GetGuid();
        Assert.Equal(antes - 530m, await Saldo(admin));
        var compra = (await Lancamentos(admin, "Depósito USD: $100.00"))[0];
        Assert.Equal("saida", compra.GetProperty("tipo").GetString());
        Assert.Equal("Compra de USD", compra.GetProperty("categoria").GetString());
        (await admin.Put($"/api/estoque/usd/compras/{loteId}", new { data = "2026-10-20", quantidade = 100, custoTotal = 540 })).EnsureSuccessStatusCode();
        Assert.Equal(antes - 540m, await Saldo(admin));
        (await admin.Post($"/api/estoque/usd/compras/{loteId}/cancelar", new { motivo = "engano" })).EnsureSuccessStatusCode();
        Assert.Equal(antes, await Saldo(admin));

        // Sincronizar é idempotente.
        var s = await (await admin.Post("/api/financeiro/sincronizar", new { })).Json();
        var depois = await Saldo(admin);
        var s2 = await (await admin.Post("/api/financeiro/sincronizar", new { })).Json();
        Assert.Equal(0, s2.GetProperty("vendas").GetInt32());
        Assert.Equal(0, s2.GetProperty("compras").GetInt32());
        Assert.Equal(depois, await Saldo(admin));
    }
}
