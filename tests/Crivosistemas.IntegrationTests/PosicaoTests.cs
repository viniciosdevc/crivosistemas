using System.Net;

namespace Crivosistemas.IntegrationTests;

[Collection("api")]
public sealed class PosicaoTests(ApiFactory factory)
{
    [Fact]
    public async Task Informar_a_conta_ajusta_o_financeiro_pela_diferenca_e_nao_repete_no_mesmo_dia()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var antes = (await (await admin.GetAsync("/api/posicao/atual")).Json()).GetProperty("patrimonio").GetProperty("contaBrl").GetDecimal();

        var r = await admin.Put("/api/posicao/hoje", new { brl = antes + 100, observacao = "conferido" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(100m, (await r.Json()).GetProperty("ajusteBrl").GetDecimal());
        var depois = (await (await admin.GetAsync("/api/posicao/atual")).Json()).GetProperty("patrimonio").GetProperty("contaBrl").GetDecimal();
        Assert.Equal(antes + 100, depois);

        // Informar o mesmo valor de novo não lança nada: o ajuste do dia continua 100 e há uma linha só.
        var r2 = await (await admin.Put("/api/posicao/hoje", new { brl = antes + 100 })).Json();
        Assert.Equal(100m, r2.GetProperty("ajusteBrl").GetDecimal());
        Assert.Equal(1, (await (await admin.GetAsync("/api/posicao")).Json()).GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Put("/api/posicao/hoje", new { })).StatusCode);
    }
}
