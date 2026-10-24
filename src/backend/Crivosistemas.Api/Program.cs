using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Auth;
using Crivosistemas.Api.Modules.Cambios;
using Crivosistemas.Api.Modules.Clientes;
using Crivosistemas.Api.Modules.Manutencao;
using Crivosistemas.Api.Modules.Acessos;
using Crivosistemas.Api.Modules.Convites;
using Crivosistemas.Api.Modules.Estoque;
using Crivosistemas.Api.Modules.Financeiro;
using Crivosistemas.Api.Modules.Integracoes;
using Crivosistemas.Api.Modules.Posicao;
using Crivosistemas.Api.Modules.Projecao;
using Crivosistemas.Api.Modules.Nfts;
using Crivosistemas.Api.Modules.Precificacao;
using Crivosistemas.Api.Modules.Vendas;
using Crivosistemas.Api.Modules.Users;
using Crivosistemas.Domain.Identity;
using Crivosistemas.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");

// Health check do contêiner: a imagem de runtime não tem curl, então a própria aplicação se consulta.
if (args.Contains("--healthcheck"))
{
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    try { Environment.Exit((await probe.GetAsync("http://127.0.0.1:8080/health/live")).IsSuccessStatusCode ? 0 : 1); }
    catch { Environment.Exit(1); }
}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console());

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Section));
builder.Services.AddEmailDispatcher();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddValidation();

// Passkeys (WebAuthn): a chave só vale para o domínio e a origem do painel, derivados de App:PublicUrl.
var publicUri = Uri.TryCreate(builder.Configuration["App:PublicUrl"], UriKind.Absolute, out var parsedUri) ? parsedUri : new Uri("http://localhost:5173");
builder.Services.AddFido2(o =>
{
    o.RPID = publicUri.Host;
    o.RPName = "Crivo Gestão";
    o.Origins = new HashSet<string> { publicUri.GetLeftPart(UriPartial.Authority) };
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

// As chaves dos cookies precisam sobreviver a reinícios e ser compartilhadas por todas as instâncias da API; o Redis as guarda quando configurado.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("crivosistemas");
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    var mux = ConnectionMultiplexer.Connect(redisConnection + ",abortConnect=false");
    builder.Services.AddSingleton<IConnectionMultiplexer>(mux);
    dataProtection.PersistKeysToStackExchangeRedis(mux, DependencyInjection.RedisPrefix + "dataprotection-keys");
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "crv_session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        // Cookie só por HTTPS em produção. Auth:RequireHttps=false permite HTTP puro, como no compose local.
        o.Cookie.SecurePolicy = builder.Configuration.GetValue("Auth:RequireHttps", !builder.Environment.IsDevelopment())
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
        o.Events.OnValidatePrincipal = Session.ValidateAsync;
        // Uma API responde com códigos de status, nunca com redirecionamento para uma página de login.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization(o =>
    o.AddPolicy(Session.AdminPolicy, p => p.RequireAuthenticatedUser().RequireRole(Session.AdminRole)));

var rateLimiting = builder.Configuration.GetValue("RateLimiting:Enabled", true);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Ip(HttpContext h) => h.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    void Policy(string name, int perMinute) => o.AddPolicy(name, h => rateLimiting
        ? RateLimitPartition.GetFixedWindowLimiter(Ip(h), _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) })
        : RateLimitPartition.GetNoLimiter(Ip(h)));
    Policy("auth", 10);
});

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).AllowCredentials().AllowAnyHeader().WithMethods("GET", "POST", "PUT", "DELETE")));

var health = builder.Services.AddHealthChecks()
    .AddMySql(builder.Configuration.GetConnectionString("Database")!, name: "mysql", tags: ["ready"]);
if (!string.IsNullOrWhiteSpace(redisConnection)) health.AddRedis(redisConnection, name: "redis", tags: ["ready"]);

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // Só o proxy reverso do próprio stack (rede privada) é confiável.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("127.0.0.0/8"));
});

var app = builder.Build();

// Comandos de operação (rodam e saem, sem subir o servidor web).
if (Array.IndexOf(args, "--seed-admin") is var sa and >= 0)
{
    if (sa + 2 >= args.Length)
    {
        Console.Error.WriteLine("Uso: --seed-admin <email> <senha> [nome]");
        return 1;
    }
    await DatabaseTasks.MigrateAsync(app.Services);
    return await DatabaseTasks.SeedAdminAsync(app.Services, args[sa + 1], args[sa + 2], sa + 3 < args.Length ? args[sa + 3] : null);
}
if (Array.IndexOf(args, "--importar-crivonft") is var ic and >= 0)
{
    DateOnly? desde = null;
    if (ic + 1 < args.Length)
    {
        if (!DateOnly.TryParseExact(args[ic + 1], "yyyy-MM-dd", out var d))
        {
            Console.Error.WriteLine("Uso: --importar-crivonft [AAAA-MM-DD]");
            return 1;
        }
        desde = d;
    }
    await DatabaseTasks.MigrateAsync(app.Services);
    return await DatabaseTasks.ImportarCrivonftAsync(app.Services, desde);
}
if (args.Contains("--migrate"))
{
    await DatabaseTasks.MigrateAsync(app.Services);
    return 0;
}
if (bool.TryParse(app.Configuration["Database:MigrateOnStartup"], out var migrateOnStartup) && migrateOnStartup) await DatabaseTasks.MigrateAsync(app.Services);

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CsrfHeaderMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference("/docs", o => o.WithTitle("Crivo Gestão API"));
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });

app.MapAuthEndpoints();
app.MapPasskeyEndpoints();
app.MapUserEndpoints();
app.MapIntegracoesEndpoints();
app.MapVendasEndpoints();
app.MapVendasManuaisEndpoints();
app.MapItensEstoqueEndpoints();
app.MapEstoqueEndpoints();
app.MapFinanceiroEndpoints();
app.MapCambiosEndpoints();
app.MapPosicaoEndpoints();
app.MapProjecaoEndpoints();
app.MapClientesEndpoints();
app.MapPrecificacaoEndpoints();
app.MapAcessosEndpoints();
app.MapManutencaoEndpoints();
app.MapConvitesEndpoints();
app.MapNftsEndpoints();

await app.RunAsync();
return 0;

public partial class Program;
