using System.Net;
using System.Text.Json;

namespace Crivosistemas.IntegrationTests;

[Collection("api")]
public sealed class PosicaoTests(ApiFactory factory)
{
    [Fact]
    public async Task Posicao_de_hoje_guarda_a_diferenca_para_o_sistema_e_substitui_no_mesmo_dia()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var s = await (await admin.GetAsync("/api/posicao/sistema")).Json();
        var brl = s.GetProperty("brl").GetDecimal();

        var r = await admin.Put("/api/posicao/hoje", new { brl = brl + 100, observacao = "conferido" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var p = await r.Json();
        Assert.Equal(100m, p.GetProperty("brlDiferenca").GetDecimal());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("usdDiferenca").ValueKind);

        // Informar de novo no mesmo dia substitui; continua uma linha só.
        (await admin.Put("/api/posicao/hoje", new { brl })).EnsureSuccessStatusCode();
        var lista = await (await admin.GetAsync("/api/posicao")).Json();
        Assert.Equal(1, lista.EnumerateArray().Count());
        Assert.Equal(0m, lista[0].GetProperty("brlDiferenca").GetDecimal());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Put("/api/posicao/hoje", new { })).StatusCode);
    }
}
