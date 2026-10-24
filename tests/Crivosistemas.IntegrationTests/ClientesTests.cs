using System.Net;
using System.Text.Json;
using Crivosistemas.Infrastructure.Integracoes;
using Microsoft.EntityFrameworkCore;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

namespace Crivosistemas.IntegrationTests;

/// <summary>Carteira (texto livre) e favorito dos clientes: ingestão, edição via crivonft, erros e filtros.</summary>
[Collection("api")]
public sealed class ClientesTests(ApiFactory factory)
{
    private static string Id() => Guid.NewGuid().ToString("N");

    private async Task<(HttpClient Admin, Guid ClienteId, string Chave)> NovoClienteAsync(string? carteira = null, bool? favorito = null, string? carteiraLoja = null)
    {
        var chave = "cli-" + Id();
        object dados = new { chave, nickname = "Nick" + chave[^8..], email = (string?)null, identidade_id = (string?)null, carteira, carteira_loja = carteiraLoja, favorito, criado_em = "2026-01-01T00:00:00Z" };
        Assert.Equal(HttpStatusCode.OK, (await factory.Maquina().Enviar(CrivoHelpers.Evento("cliente.upsert", dados))).Status);
        var admin = await factory.SeedAndLoginAdminAsync();
        var lista = await (await admin.GetAsync($"/api/clientes?busca={chave}")).Json();
        return (admin, Assert.Single(lista.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid(), chave);
    }

    private static async Task<JsonElement> Detalhe(HttpClient admin, Guid id) => (await (await admin.GetAsync($"/api/clientes/{id}")).Json()).GetProperty("cliente");

    [Fact]
    public async Task Evento_grava_carteira_livre_favorito_e_carteira_da_loja_e_null_nao_apaga()
    {
        var loja = "0x" + new string('a', 40);
        var (admin, id, chave) = await NovoClienteAsync("  carteira da Maria  ", true, loja.ToUpperInvariant().Replace("0X", "0x"));
        var c = await Detalhe(admin, id);
        Assert.Equal("carteira da Maria", c.GetProperty("carteira").GetString());
        Assert.False(c.GetProperty("carteiraValida").GetBoolean());
        Assert.True(c.GetProperty("favorito").GetBoolean());
        Assert.Equal(loja, c.GetProperty("carteiraLoja").GetString());

        // null (ou campos ausentes) nunca apaga; favorito false explícito desliga.
        var maquina = factory.Maquina();
        Assert.Equal(HttpStatusCode.OK, (await maquina.Enviar(CrivoHelpers.Evento("cliente.upsert", new { chave, nickname = "x", carteira = (string?)null, favorito = (bool?)null, carteira_loja = (string?)null }))).Status);
        c = await Detalhe(admin, id);
        Assert.Equal("carteira da Maria", c.GetProperty("carteira").GetString());
        Assert.True(c.GetProperty("favorito").GetBoolean());
        Assert.Equal(loja, c.GetProperty("carteiraLoja").GetString());
        Assert.Equal(HttpStatusCode.OK, (await maquina.Enviar(CrivoHelpers.Evento("cliente.upsert", new { chave, nickname = "x", favorito = false }))).Status);
        Assert.False((await Detalhe(admin, id)).GetProperty("favorito").GetBoolean());

        // Endereço válido vem em minúsculas e marcado como válido.
        var end = "0xAbCdEf" + new string('1', 34);
        await maquina.Enviar(CrivoHelpers.Evento("cliente.upsert", new { chave, nickname = "x", carteira = end }));
        c = await Detalhe(admin, id);
        Assert.Equal(end.ToLowerInvariant(), c.GetProperty("carteira").GetString());
        Assert.True(c.GetProperty("carteiraValida").GetBoolean());
    }

    [Fact]
    public async Task Patch_com_crivonft_chama_a_rota_assinada_e_grava_a_resposta()
    {
        var f = factory.Crivonft;
        var (admin, id, _) = await NovoClienteAsync("antiga");
        var nick = (await Detalhe(admin, id)).GetProperty("nickname").GetString();
        f.ChamadasGestao.Clear();
        f.RedeForaGestao = false;
        f.RespostaGestao = new GestaoResposta(200, $$$"""{"ok":true,"cliente":{"nickname":"{{{nick}}}","carteira":"texto 0x123 livre","favorito":true}}""");

        var r = await admin.Patch($"/api/clientes/{id}", """{"carteira":"  texto 0x123 livre ","favorito":true}""");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var corpo = await r.Json();
        Assert.Equal("texto 0x123 livre", corpo.GetProperty("carteira").GetString());
        Assert.False(corpo.GetProperty("carteiraValida").GetBoolean());
        Assert.True(corpo.GetProperty("favorito").GetBoolean());
        var chamada = Assert.Single(f.ChamadasGestao);
        Assert.Equal(HttpMethod.Put, chamada.Metodo);
        Assert.Equal("/api/integracoes/gestao/clientes", chamada.Caminho);
        var enviado = JsonDocument.Parse(chamada.Corpo!).RootElement;
        Assert.Equal(nick, enviado.GetProperty("nickname").GetString());
        Assert.Equal("texto 0x123 livre", enviado.GetProperty("carteira").GetString());
        Assert.True(enviado.GetProperty("favorito").GetBoolean());

        // Só favorito: a carteira não vai no corpo nem muda.
        f.ChamadasGestao.Clear();
        f.RespostaGestao = new GestaoResposta(200, $$$"""{"ok":true,"cliente":{"nickname":"{{{nick}}}","carteira":"texto 0x123 livre","favorito":false}}""");
        Assert.Equal(HttpStatusCode.OK, (await admin.Patch($"/api/clientes/{id}", """{"favorito":false}""")).StatusCode);
        Assert.False(JsonDocument.Parse(Assert.Single(f.ChamadasGestao).Corpo!).RootElement.TryGetProperty("carteira", out _));
        var c = await Detalhe(admin, id);
        Assert.False(c.GetProperty("favorito").GetBoolean());
        Assert.Equal("texto 0x123 livre", c.GetProperty("carteira").GetString());

        // Carteira vazia remove (null no corpo enviado ao crivonft).
        f.ChamadasGestao.Clear();
        f.RespostaGestao = new GestaoResposta(200, $$$"""{"ok":true,"cliente":{"nickname":"{{{nick}}}","carteira":null,"favorito":false}}""");
        Assert.Equal(HttpStatusCode.OK, (await admin.Patch($"/api/clientes/{id}", """{"carteira":"  "}""")).StatusCode);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(Assert.Single(f.ChamadasGestao).Corpo!).RootElement.GetProperty("carteira").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await Detalhe(admin, id)).GetProperty("carteira").ValueKind);

        // Auditoria.
        using var escopo = factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "clientes.atualizado" && a.EntityId == id));
    }

    [Fact]
    public async Task Patch_valida_e_aceita_0x_invalido_so_marcando_invalida()
    {
        var f = factory.Crivonft;
        var (admin, id, _) = await NovoClienteAsync();
        f.ChamadasGestao.Clear();
        f.RedeForaGestao = false;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Patch($"/api/clientes/{id}", "{}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Patch($"/api/clientes/{id}", $$"""{"carteira":"{{new string('x', 121)}}"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Patch($"/api/clientes/{id}", """{"favorito":"sim"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Patch($"/api/clientes/{Guid.NewGuid()}", """{"favorito":true}""")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().Patch($"/api/clientes/{id}", """{"favorito":true}""")).StatusCode);
        Assert.Empty(f.ChamadasGestao);

        f.RespostaGestao = new GestaoResposta(200, """{"ok":true,"cliente":{"nickname":"x","carteira":"0x12ZZ","favorito":false}}""");
        var r = await admin.Patch($"/api/clientes/{id}", """{"carteira":"0x12ZZ"}""");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var corpo = await r.Json();
        Assert.Equal("0x12ZZ", corpo.GetProperty("carteira").GetString());
        Assert.False(corpo.GetProperty("carteiraValida").GetBoolean());
    }

    [Fact]
    public async Task Operador_tambem_edita()
    {
        var f = factory.Crivonft;
        var (admin, id, _) = await NovoClienteAsync();
        var operador = await factory.CreateAndLoginAsync(admin, "operator");
        f.RedeForaGestao = false;
        f.RespostaGestao = new GestaoResposta(200, """{"ok":true,"cliente":{"nickname":"x","carteira":null,"favorito":true}}""");
        Assert.Equal(HttpStatusCode.OK, (await operador.Patch($"/api/clientes/{id}", """{"favorito":true}""")).StatusCode);
    }

    [Theory]
    [InlineData(404, "publique a atualização")]
    [InlineData(401, "Token ou segredo")]
    [InlineData(403, "Token ou segredo")]
    [InlineData(500, "erro 500")]
    public async Task Erro_do_crivonft_vira_502_com_detalhe_e_nao_grava(int statusCrivonft, string trecho)
    {
        var f = factory.Crivonft;
        var (admin, id, _) = await NovoClienteAsync("original", false);
        f.RedeForaGestao = false;
        f.RespostaGestao = new GestaoResposta(statusCrivonft, """{"error":"x"}""");
        var r = await admin.Patch($"/api/clientes/{id}", """{"carteira":"nova","favorito":true}""");
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        Assert.Contains(trecho, (await r.Body()).GetProperty("detail").GetString());
        var c = await Detalhe(admin, id);
        Assert.Equal("original", c.GetProperty("carteira").GetString());
        Assert.False(c.GetProperty("favorito").GetBoolean());
    }

    [Fact]
    public async Task Recusa_422_do_crivonft_repassa_a_mensagem_e_rede_fora_vira_502()
    {
        var f = factory.Crivonft;
        var (admin, id, _) = await NovoClienteAsync();
        f.RedeForaGestao = false;
        f.RespostaGestao = new GestaoResposta(422, """{"error":"Cliente sem nickname; edite no crivonft."}""");
        var r = await admin.Patch($"/api/clientes/{id}", """{"favorito":true}""");
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        Assert.Equal("Cliente sem nickname; edite no crivonft.", (await r.Body()).GetProperty("detail").GetString());

        f.RedeForaGestao = true;
        r = await admin.Patch($"/api/clientes/{id}", """{"favorito":true}""");
        f.RedeForaGestao = false;
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        Assert.Contains("tempo esgotado", (await r.Body()).GetProperty("detail").GetString());
        Assert.False((await Detalhe(admin, id)).GetProperty("favorito").GetBoolean());
    }

    [Fact]
    public async Task Sem_integracao_configurada_grava_so_local()
    {
        var f = factory.Crivonft;
        var (_, id, _) = await NovoClienteAsync();
        f.ChamadasGestao.Clear();
        var email = $"admin-{Id()}"[..16] + "@teste.dev";
        await factory.SeedAndLoginAdminAsync(email);
        using var semConfig = factory.WithWebHostBuilder(b => b.UseSetting("Integrations:Crivonft:BaseUrl", ""));
        var admin = await semConfig.LoginComo(email);
        var r = await admin.Patch($"/api/clientes/{id}", """{"carteira":"só aqui","favorito":true}""");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Empty(f.ChamadasGestao);
        var corpo = await r.Json();
        Assert.Equal("só aqui", corpo.GetProperty("carteira").GetString());
        Assert.True(corpo.GetProperty("favorito").GetBoolean());
    }

    [Fact]
    public async Task Filtros_de_favorito_carteira_e_ordenacao_com_favoritos_primeiro()
    {
        var marca = Id()[..8];
        var maquina = factory.Maquina();
        async Task Cria(string nome, string? carteira, bool favorito) =>
            Assert.Equal(HttpStatusCode.OK, (await maquina.Enviar(CrivoHelpers.Evento("cliente.upsert", new { chave = $"{marca}-{nome}", nickname = $"{marca}-{nome}", carteira, favorito }))).Status);
        await Cria("a", null, false);
        await Cria("b", "livre", true);
        await Cria("c", "0x" + new string('b', 40), false);
        await Cria("d", null, true);
        var admin = await factory.SeedAndLoginAdminAsync();

        async Task<string[]> Nicks(string query) =>
            (await (await admin.GetAsync($"/api/clientes?busca={marca}&{query}")).Json()).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("nickname").GetString()![(marca.Length + 1)..]).ToArray();

        Assert.Equal(["a", "b", "c", "d"], await Nicks(""));
        Assert.Equal(["b", "d"], await Nicks("favorito=true"));
        Assert.Equal(["b", "c"], await Nicks("carteira=com"));
        Assert.Equal(["a", "d"], await Nicks("carteira=sem"));
        Assert.Equal(["d"], await Nicks("favorito=true&carteira=sem"));
        Assert.Equal(["b", "d", "a", "c"], await Nicks("ordenar=favoritos"));
    }
}
