using System.Net;

namespace Crivosistemas.IntegrationTests;

[Collection("api")]
public sealed class ProjecaoTests(ApiFactory factory)
{
    [Fact]
    public async Task Cenario_fixa_a_media_do_periodo_base_projeta_o_lucro_e_compara_com_o_realizado()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        // Base em 2018 (dias isolados dos outros testes): 10 dias, uma venda de 1000 com lucro 100 → média de 100 de faturamento e 10 de lucro por dia.
        await factory.Maquina().Enviar(CrivoHelpers.Evento("venda.upsert",
            CrivoHelpers.Venda("proj-1", new DateTime(2018, 3, 3, 15, 0, 0, DateTimeKind.Utc), 1000m, lucro: 100m)));

        var corpo = new { nome = "Teste base", baseDe = "2018-03-01", baseAte = "2018-03-10", dataFinal = "2018-03-20", crescimentoPercent = 10 };
        var sim = await (await admin.Post("/api/projecoes/simular", corpo)).Json();
        Assert.Equal(10m, sim.GetProperty("cenario").GetProperty("mediaLucroDia").GetDecimal());
        Assert.Equal(10, sim.GetProperty("dias").GetArrayLength());

        var criado = await admin.Post("/api/projecoes", corpo);
        Assert.Equal(HttpStatusCode.OK, criado.StatusCode);
        var det = await criado.Json();
        var id = det.GetProperty("cenario").GetProperty("id").GetGuid();
        Assert.Equal("2018-03-11", det.GetProperty("cenario").GetProperty("inicioProjecao").GetString());
        Assert.Equal(110m, det.GetProperty("resumo").GetProperty("lucroFinal").GetDecimal()); // 10 dias × 10 × 1,10
        // Tudo já passou e não houve venda no período projetado: realizado 0, esperado 110.
        Assert.Equal(0m, det.GetProperty("resumo").GetProperty("lucroRealizadoAteHoje").GetDecimal());
        Assert.Equal(-110m, det.GetProperty("resumo").GetProperty("diferencaLucro").GetDecimal());

        Assert.Contains((await (await admin.GetAsync("/api/projecoes")).Json()).EnumerateArray(), x => x.GetProperty("cenario").GetProperty("id").GetGuid() == id);

        var editado = await (await admin.Put($"/api/projecoes/{id}", corpo with { crescimentoPercent = 0 })).Json();
        Assert.Equal(100m, editado.GetProperty("resumo").GetProperty("lucroFinal").GetDecimal());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/projecoes", corpo with { dataFinal = "2018-03-10" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/projecoes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/projecoes/{id}")).StatusCode);
    }
}
