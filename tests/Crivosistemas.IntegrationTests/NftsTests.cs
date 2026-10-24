using System.Net;
using Crivosistemas.Infrastructure.Integracoes;

namespace Crivosistemas.IntegrationTests;

/// <summary>NFTs e coleções passam pelo crivonft: aqui conferimos permissão, o que é repassado e como os erros voltam.</summary>
[Collection("api")]
public sealed class NftsTests(ApiFactory factory)
{
    [Fact]
    public async Task Somente_admin_e_repassa_consulta_e_acoes_ao_crivonft()
    {
        var f = factory.Crivonft;
        f.ChamadasGestao.Clear();
        f.RespostaGestao = new GestaoResposta(200, """{"itens":[],"total":0}""");
        var admin = await factory.SeedAndLoginAdminAsync();
        var operador = await factory.CreateAndLoginAsync(admin, "operator");

        Assert.Equal(HttpStatusCode.Forbidden, (await operador.GetAsync("/api/nfts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.Post("/api/nfts/colecoes", new { nome = "abc" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/nfts/colecoes")).StatusCode);
        Assert.Empty(f.ChamadasGestao);

        var lista = await admin.GetAsync("/api/nfts?categoria=pets&q=gato%20preto&apenasMudaram=1&page=2&lixo=x");
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        Assert.Equal(0, (await lista.Json()).GetProperty("total").GetInt32());
        var chamada = Assert.Single(f.ChamadasGestao);
        Assert.Equal(HttpMethod.Get, chamada.Metodo);
        Assert.Equal("/api/integracoes/gestao/nfts?categoria=pets&q=gato%20preto&apenasMudaram=1&page=2", chamada.Caminho);

        f.ChamadasGestao.Clear();
        f.RespostaGestao = new GestaoResposta(202, """{"ok":true,"iniciada":true}""");
        Assert.Equal(HttpStatusCode.Accepted, (await admin.Post("/api/nfts/atualizar", new { acao = "catalogo" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/nfts/atualizar", new { acao = "apagar-tudo" })).StatusCode);
        Assert.Equal("""{"acao":"catalogo"}""", Assert.Single(f.ChamadasGestao).Corpo);
    }

    [Fact]
    public async Task Colecoes_validam_localmente_devolvem_a_mensagem_do_crivonft_e_explicam_crivonft_desatualizado()
    {
        var f = factory.Crivonft;
        var admin = await factory.SeedAndLoginAdminAsync();

        f.ChamadasGestao.Clear();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/nfts/colecoes", new { nome = "", totalPosicoes = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/nfts/colecoes", new { nome = "Pets", totalPosicoes = 501 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/nfts/colecoes", new { nome = "Pets", contratoOverride = "0x123" })).StatusCode);
        Assert.Empty(f.ChamadasGestao);

        f.RespostaGestao = new GestaoResposta(422, """{"error":"Categoria inválida."}""");
        var recusada = await admin.Post("/api/nfts/colecoes", new { nome = "Pets", categoria = "xyz" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, recusada.StatusCode);
        Assert.Equal("Categoria inválida.", (await recusada.Body()).GetProperty("title").GetString());

        f.ChamadasGestao.Clear();
        f.RespostaGestao = new GestaoResposta(201, """{"ok":true,"id":"11111111-1111-1111-1111-111111111111"}""");
        var criada = await admin.Post("/api/nfts/colecoes", new { nome = "  Pets raros ", categoria = "pets", totalPosicoes = 20 });
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        Assert.Equal("""{"nome":"Pets raros","categoria":"pets","contratoOverride":null,"totalPosicoes":20}""", Assert.Single(f.ChamadasGestao).Corpo);

        f.RespostaGestao = new GestaoResposta(404, "<html>nada</html>");
        var antigo = await admin.GetAsync("/api/nfts/colecoes");
        Assert.Equal(HttpStatusCode.BadGateway, antigo.StatusCode);
        Assert.Contains("publique a atualização", (await antigo.Body()).GetProperty("detail").GetString());
        f.RespostaGestao = new GestaoResposta(200, "{}");
    }

    [Fact]
    public async Task Habbo_repassa_estado_testar_enviar_e_config_so_para_admin()
    {
        var f = factory.Crivonft;
        var admin = await factory.SeedAndLoginAdminAsync();
        var operador = await factory.CreateAndLoginAsync(admin, "operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.GetAsync("/api/nfts/habbo")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.Post("/api/nfts/habbo/enviar", new { })).StatusCode);

        f.ChamadasGestao.Clear();
        f.RespostaGestao = new GestaoResposta(200, """{"chaves":{"leitura":true,"escrita":true},"destinos":[],"envios":[]}""");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/nfts/habbo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Post("/api/nfts/habbo/testar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Put("/api/nfts/habbo/config", new { roomId = "12345", envioAutomatico = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Put("/api/nfts/habbo/config", new { roomId = "r-hhes-1" })).StatusCode);
        f.RespostaGestao = new GestaoResposta(202, """{"ok":true,"iniciado":2}""");
        Assert.Equal(HttpStatusCode.Accepted, (await admin.Post("/api/nfts/habbo/enviar", new { })).StatusCode);

        Assert.Equal(4, f.ChamadasGestao.Count);
        Assert.Equal("""{"acao":"config","hotel":null,"roomId":"12345","envioAutomatico":false}""", f.ChamadasGestao[2].Corpo);
        Assert.Equal("""{"acao":"enviar","destinoId":null}""", f.ChamadasGestao[3].Corpo);
        f.RespostaGestao = new GestaoResposta(200, "{}");
    }
}

