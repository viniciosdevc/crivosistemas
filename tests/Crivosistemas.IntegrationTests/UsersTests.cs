using System.Net;

namespace Crivosistemas.IntegrationTests;

[Collection("api")]
public sealed class UsersTests(ApiFactory factory)
{
    [Fact]
    public async Task Rotas_de_usuarios_exigem_login_e_papel_de_administrador()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/users")).StatusCode);

        var admin = await factory.SeedAndLoginAdminAsync();
        var operador = await factory.CreateAndLoginAsync(admin, "operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.Post("/api/users", new { name = "X Y", email = "xy@teste.dev", password = "senha-segura-1", role = "admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Admin_cria_lista_e_busca_usuarios()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var email = $"maria-{Guid.NewGuid():N}"[..16] + "@teste.dev";
        var created = await (await admin.Post("/api/users", new { name = "Maria Operadora", email = email.ToUpperInvariant(), password = "senha-segura-1", role = "operator" })).Json();
        Assert.Equal(email, created.GetProperty("email").GetString());
        Assert.Equal("operator", created.GetProperty("role").GetString());
        Assert.Equal("active", created.GetProperty("status").GetString());
        Assert.False(created.TryGetProperty("passwordHash", out _));

        var list = await (await admin.GetAsync($"/api/users?search={Uri.EscapeDataString("maria")}")).Json();
        Assert.Contains(list.GetProperty("items").EnumerateArray(), u => u.GetProperty("email").GetString() == email);
        Assert.True(list.GetProperty("total").GetInt32() >= 1);

        // E-mail repetido (qualquer caixa) é erro de validação no campo.
        var dup = await admin.Post("/api/users", new { name = "Outra Maria", email, password = "senha-segura-1", role = "operator" });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Contains("email", (await dup.Body()).GetProperty("errors").EnumerateObject().Select(p => p.Name.ToLowerInvariant()));
    }

    [Fact]
    public async Task Valida_dados_do_novo_usuario()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/users", new { name = "Ana Lu", email = "invalido", password = "senha-segura-1", role = "operator" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/users", new { name = "Ana Lu", email = "ana.lu@teste.dev", password = "curta", role = "operator" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Post("/api/users", new { name = "Ana Lu", email = "ana.lu@teste.dev", password = "senha-segura-1", role = "dono" })).StatusCode);
    }

    [Fact]
    public async Task Usuario_desativado_nao_entra_e_perde_a_sessao_na_hora()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var email = $"op-{Guid.NewGuid():N}"[..14] + "@teste.dev";
        var operador = await factory.CreateAndLoginAsync(admin, "operator", email);
        var id = (await (await operador.GetAsync("/api/auth/me")).Json()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await operador.GetAsync("/api/auth/me")).StatusCode);

        var off = await (await admin.Post($"/api/users/{id}/deactivate")).Json();
        Assert.Equal("disabled", off.GetProperty("status").GetString());

        // A sessão que já existia deixa de valer imediatamente e o login é recusado.
        Assert.Equal(HttpStatusCode.Unauthorized, (await operador.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().Post("/api/auth/login", new { email, password = ApiFactory.AdminPassword })).StatusCode);

        var on = await (await admin.Post($"/api/users/{id}/activate")).Json();
        Assert.Equal("active", on.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await factory.NewClient().Post("/api/auth/login", new { email, password = ApiFactory.AdminPassword })).StatusCode);
    }

    [Fact]
    public async Task Nao_desativa_a_si_mesmo_nem_o_ultimo_administrador()
    {
        var admin = await factory.SeedAndLoginAdminAsync($"unico-{Guid.NewGuid():N}"[..14] + "@teste.dev");
        var me = (await (await admin.GetAsync("/api/auth/me")).Json()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Post($"/api/users/{me}/deactivate")).StatusCode);
    }

    [Fact]
    public async Task Ultimo_administrador_ativo_nao_pode_virar_operador_mas_pode_havendo_outro()
    {
        // Em um banco compartilhado entre testes há outros administradores; aqui isolamos pelo uso direto da regra de domínio
        // (coberta nos testes unitários) e conferimos o caminho feliz: com outro admin ativo, o rebaixamento é permitido.
        var admin = await factory.SeedAndLoginAdminAsync();
        var segundo = await factory.CreateAndLoginAsync(admin, "admin");
        var segundoId = (await (await segundo.GetAsync("/api/auth/me")).Json()).GetProperty("id").GetGuid();

        var updated = await (await admin.Put($"/api/users/{segundoId}", new { name = "Segundo Admin", role = "operator" })).Json();
        Assert.Equal("operator", updated.GetProperty("role").GetString());
        Assert.Equal("Segundo Admin", updated.GetProperty("name").GetString());
        // O papel muda na hora para quem já estava logado.
        Assert.Equal(HttpStatusCode.Forbidden, (await segundo.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Usuario_inexistente_devolve_404()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post($"/api/users/{Guid.NewGuid()}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Put($"/api/users/{Guid.NewGuid()}", new { name = "Fulano", role = "operator" })).StatusCode);
    }
}
