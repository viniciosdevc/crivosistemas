using System.Net;
using Crivosistemas.Domain.Metas;
using Crivosistemas.Domain.Vendas;

namespace Crivosistemas.IntegrationTests;

[Collection("api")]
public sealed class MetasTests(ApiFactory factory)
{
    [Fact]
    public async Task Admin_define_metas_de_varias_semanas_e_o_painel_reflete()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var segunda = RegrasMetas.Segunda(FusoSp.Hoje());

        var inicial = await (await admin.GetAsync("/api/metas-semanais")).Json();
        Assert.Equal(segunda.ToString("yyyy-MM-dd"), inicial.GetProperty("semanaInicio").GetString());
        Assert.Equal(1000m, inicial.GetProperty("resumo").GetProperty("metaBrl").GetDecimal()); // padrão
        Assert.Equal(12, inicial.GetProperty("planejamento").GetArrayLength());
        Assert.Equal(7 * 16, inicial.GetProperty("dias").GetArrayLength());

        var salvo = await admin.Put("/api/metas-semanais", new
        {
            metas = new[]
            {
                new { semanaInicio = segunda.ToString("yyyy-MM-dd"), valorBrl = 2500.50m },
                new { semanaInicio = segunda.AddDays(7).ToString("yyyy-MM-dd"), valorBrl = 3000m },
            },
        });
        Assert.Equal(HttpStatusCode.OK, salvo.StatusCode);
        var painel = await salvo.Json();
        Assert.Equal(2500.50m, painel.GetProperty("resumo").GetProperty("metaBrl").GetDecimal());
        var plan = painel.GetProperty("planejamento");
        Assert.True(plan[0].GetProperty("definida").GetBoolean());
        Assert.Equal(3000m, plan[1].GetProperty("metaBrl").GetDecimal());
        Assert.False(plan[2].GetProperty("definida").GetBoolean());
        Assert.Equal(1000m, plan[2].GetProperty("metaBrl").GetDecimal());

        // Salvar de novo a mesma semana atualiza (não duplica).
        var atualizado = await (await admin.Put("/api/metas-semanais", new { metas = new[] { new { semanaInicio = segunda.ToString("yyyy-MM-dd"), valorBrl = 4000m } } })).Json();
        Assert.Equal(4000m, atualizado.GetProperty("resumo").GetProperty("metaBrl").GetDecimal());
    }

    [Fact]
    public async Task Rejeita_semana_que_nao_e_segunda_e_valor_invalido_e_operador_nao_cadastra()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var segunda = RegrasMetas.Segunda(FusoSp.Hoje());

        var terca = await admin.Put("/api/metas-semanais", new { metas = new[] { new { semanaInicio = segunda.AddDays(1).ToString("yyyy-MM-dd"), valorBrl = 500m } } });
        Assert.Equal(HttpStatusCode.BadRequest, terca.StatusCode);
        var zero = await admin.Put("/api/metas-semanais", new { metas = new[] { new { semanaInicio = segunda.ToString("yyyy-MM-dd"), valorBrl = 0m } } });
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        var vazio = await admin.Put("/api/metas-semanais", new { metas = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, vazio.StatusCode);

        var operador = await factory.CreateAndLoginAsync(admin, "operator");
        Assert.Equal(HttpStatusCode.OK, (await operador.GetAsync("/api/metas-semanais")).StatusCode);
        var negado = await operador.Put("/api/metas-semanais", new { metas = new[] { new { semanaInicio = segunda.ToString("yyyy-MM-dd"), valorBrl = 500m } } });
        Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);
    }
}
