using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crivosistemas.Infrastructure.Email;
using Crivosistemas.Infrastructure.Integracoes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySql.Data.MySqlClient;

namespace Crivosistemas.IntegrationTests;

/// <summary>Sobe a API real contra um banco MySQL descartável (CRIVOSISTEMAS_TEST_DB_SERVER, padrão localhost).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _database = $"crivosistemas_test_{Guid.NewGuid():N}"[..32];
    private readonly string _server = Environment.GetEnvironmentVariable("CRIVOSISTEMAS_TEST_DB_SERVER")
        ?? "Server=localhost;Port=3306;User=crivosistemas;Password=crivosistemas;";

    public const string AdminEmail = "admin@teste.dev";
    public const string AdminPassword = "senha-segura-1";
    public const string PublicUrl = "https://crivo.test";
    public const string CrivoToken = "token-de-teste";
    public const string CrivoSegredo = "segredo-hmac-de-teste";

    /// <summary>Substitui o HttpClient do crivonft: os testes preparam as respostas e inspecionam os envios.</summary>
    public FakeCrivonftClient Crivonft { get; } = new();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public CapturingEmailSender Emails { get; } = new();
    public string ConnectionString => $"{_server}Database={_database};";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", ConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", "");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("App:PublicUrl", PublicUrl);
        builder.UseSetting("Integrations:Crivonft:BaseUrl", "https://crivonft.test");
        builder.UseSetting("Integrations:Crivonft:Token", CrivoToken);
        builder.UseSetting("Integrations:Crivonft:HmacSecret", CrivoSegredo);
        builder.UseSetting("Integrations:Crivonft:ExportToken", "export-de-teste");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<ICrivonftClient>();
            services.AddSingleton<ICrivonftClient>(Crivonft);
        });
    }

    public HttpClient NewClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    /// <summary>Cria o administrador pelo mesmo caminho do comando --seed-admin e devolve um cliente já autenticado.</summary>
    public async Task<HttpClient> SeedAndLoginAdminAsync(string? email = null)
    {
        email ??= $"admin-{Guid.NewGuid():N}"[..16] + "@teste.dev";
        Assert.Equal(0, await Crivosistemas.Api.Common.DatabaseTasks.SeedAdminAsync(Services, email, AdminPassword, "Admin Teste"));
        var client = NewClient();
        (await client.Post("/api/auth/login", new { email, password = AdminPassword })).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Cria um usuário pela API (como administrador) e devolve um cliente autenticado como ele.</summary>
    public async Task<HttpClient> CreateAndLoginAsync(HttpClient admin, string role, string? email = null, string password = AdminPassword)
    {
        email ??= $"{role}-{Guid.NewGuid():N}"[..16] + "@teste.dev";
        (await admin.Post("/api/users", new { name = "Pessoa Teste", email, password, role })).EnsureSuccessStatusCode();
        var client = NewClient();
        (await client.Post("/api/auth/login", new { email, password })).EnsureSuccessStatusCode();
        return client;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await using var conn = new MySqlConnection(_server);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand($"DROP DATABASE IF EXISTS `{_database}`", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;

public static class HttpExtensions
{
    public static async Task<T> ReadAs<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"Esperava sucesso mas veio {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, ApiFactory.Json)!;
    }

    public static Task<HttpResponseMessage> Post(this HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(url, body ?? new { }, ApiFactory.Json);

    public static Task<HttpResponseMessage> Put(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, ApiFactory.Json);

    /// <summary>Lê o corpo como JSON mesmo em respostas de erro (Json() exige sucesso).</summary>
    public static async Task<JsonElement> Body(this HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), ApiFactory.Json);

    public static async Task<JsonElement> Json(this HttpResponseMessage response) => await response.ReadAs<JsonElement>();
}
