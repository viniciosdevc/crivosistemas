using System.Net;
using Crivosistemas.Api.Common;

namespace Crivosistemas.IntegrationTests;

[Collection("api")]
public sealed class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Nao_existe_cadastro_publico()
    {
        var client = factory.NewClient();
        var res = await client.Post("/api/auth/register", new { name = "X", email = "x@teste.dev", password = "senha-segura-1" });
        Assert.True(res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Seed_admin_cria_o_primeiro_administrador_uma_unica_vez()
    {
        var email = $"seed-{Guid.NewGuid():N}"[..14] + "@teste.dev";
        Assert.Equal(0, await DatabaseTasks.SeedAdminAsync(factory.Services, email, "senha-segura-1", null));
        // Segunda execução não altera nada.
        Assert.Equal(0, await DatabaseTasks.SeedAdminAsync(factory.Services, email, "outra-senha-9", null));

        var client = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Post("/api/auth/login", new { email, password = "outra-senha-9" })).StatusCode);
        var me = await (await client.Post("/api/auth/login", new { email = email.ToUpperInvariant(), password = "senha-segura-1" })).Json();
        Assert.Equal("admin", me.GetProperty("role").GetString());
        Assert.Equal("Administrador", me.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Seed_admin_rejeita_senha_curta_e_email_invalido()
    {
        Assert.Equal(1, await DatabaseTasks.SeedAdminAsync(factory.Services, "ok@teste.dev", "curta", null));
        Assert.Equal(1, await DatabaseTasks.SeedAdminAsync(factory.Services, "nao-e-email", "senha-segura-1", null));
    }

    [Fact]
    public async Task Login_erra_do_mesmo_jeito_para_conta_inexistente_e_senha_errada()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var me = await (await admin.GetAsync("/api/auth/me")).Json();
        var email = me.GetProperty("email").GetString();

        var wrongPassword = await factory.NewClient().Post("/api/auth/login", new { email, password = "errada-errada" });
        var unknown = await factory.NewClient().Post("/api/auth/login", new { email = "ninguem@teste.dev", password = "errada-errada" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await wrongPassword.Body()).GetProperty("title").GetString(), (await unknown.Body()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Me_exige_login_e_logout_encerra_a_sessao()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/auth/me")).StatusCode);
        var client = await factory.SeedAndLoginAdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.Post("/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Requisicao_que_altera_dados_sem_cabecalho_csrf_e_recusada()
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var res = await client.Post("/api/auth/login", new { email = "a@b.com", password = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Troca_de_senha_exige_a_senha_atual()
    {
        var client = await factory.SeedAndLoginAdminAsync();
        var email = (await (await client.GetAsync("/api/auth/me")).Json()).GetProperty("email").GetString();

        var wrong = await client.Post("/api/auth/change-password", new { currentPassword = "nao-e-essa", newPassword = "nova-senha-123" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var short_ = await client.Post("/api/auth/change-password", new { currentPassword = ApiFactory.AdminPassword, newPassword = "curta" });
        Assert.Equal(HttpStatusCode.BadRequest, short_.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.Post("/api/auth/change-password", new { currentPassword = ApiFactory.AdminPassword, newPassword = "nova-senha-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().Post("/api/auth/login", new { email, password = ApiFactory.AdminPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.NewClient().Post("/api/auth/login", new { email, password = "nova-senha-123" })).StatusCode);
    }

    [Fact]
    public async Task Redefinir_senha_por_email_funciona_uma_vez()
    {
        var client = await factory.SeedAndLoginAdminAsync();
        var email = (await (await client.GetAsync("/api/auth/me")).Json()).GetProperty("email").GetString()!;
        var anon = factory.NewClient();

        // Sempre 204, mesmo para e-mail desconhecido.
        Assert.Equal(HttpStatusCode.NoContent, (await anon.Post("/api/auth/forgot-password", new { email = "ninguem@teste.dev" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anon.Post("/api/auth/forgot-password", new { email })).StatusCode);
        var token = await factory.Emails.TokenFor(email, "/redefinir-senha");

        Assert.Equal(HttpStatusCode.NoContent, (await anon.Post("/api/auth/reset-password", new { token, password = "senha-redefinida-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.Post("/api/auth/reset-password", new { token, password = "outra-senha-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.NewClient().Post("/api/auth/login", new { email, password = "senha-redefinida-1" })).StatusCode);
    }

    [Fact]
    public async Task Health_responde_sem_login()
    {
        var client = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }
}
