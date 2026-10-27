using Crivosistemas.Infrastructure.Email;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Crivosistemas.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Prefixo de todas as chaves do Redis. O Redis pode ser compartilhado com o crivonft, então nada aqui vive fora dele.</summary>
    public const string RedisPrefix = "crivosistemas:";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database is not configured.");

        services.AddDbContext<AppDbContext>(o => o.UseMySQL(connectionString,
            mysql => mysql.EnableRetryOnFailure(3).MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        var redis = config.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
            services.AddDistributedMemoryCache();
        else
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redis;
                o.InstanceName = RedisPrefix;
            });

        // Lido à mão de propósito: implantações costumam declarar toda variável com valor vazio (SMTP_PORT=), e o binder
        // padrão lança exceção para "" em números/booleanos, o que derrubaria a API na partida.
        var smtp = ReadSmtp(config.GetSection(SmtpOptions.Section));
        services.Configure<SmtpOptions>(o =>
        {
            o.Host = smtp.Host; o.Port = smtp.Port; o.Username = smtp.Username; o.Password = smtp.Password;
            o.UseSsl = smtp.UseSsl; o.FromAddress = smtp.FromAddress; o.FromName = smtp.FromName;
        });
        if (smtp.IsConfigured)
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, LogEmailSender>();
        services.AddSingleton<EmailQueue>();

        // Integração com o crivonft. Sem configuração, os endpoints respondem 503 e o worker não envia nada.
        var cv = config.GetSection(CrivonftOptions.Section);
        services.Configure<CrivonftOptions>(o =>
        {
            o.BaseUrl = cv["BaseUrl"]?.Trim().TrimEnd('/') ?? "";
            o.Token = cv["Token"]?.Trim() ?? "";
            o.HmacSecret = cv["HmacSecret"]?.Trim() ?? "";
            o.ExportToken = cv["ExportToken"]?.Trim() ?? "";
        });
        services.AddMemoryCache();
        services.AddHttpClient<ICrivonftClient, CrivonftClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IngestaoService>();
        services.AddScoped<ReconciliacaoService>();
        services.AddScoped<PrecificacaoService>();
        services.AddScoped<Estoque.EstoqueService>();
        services.AddScoped<Estoque.RecalculoCustoService>();
        services.AddScoped<Financeiro.FinanceiroService>();
        services.AddScoped<Cambios.CambiosService>();
        services.AddScoped<Posicao.PosicaoService>();
        services.AddScoped<PrecificacaoEnvioService>();
        services.AddScoped<VendaEnvioService>();
        return services;
    }

    private static SmtpOptions ReadSmtp(IConfigurationSection s) => new()
    {
        Host = s["Host"]?.Trim() ?? "",
        Port = int.TryParse(s["Port"], out var port) && port > 0 ? port : 587,
        Username = s["Username"] ?? "",
        Password = s["Password"] ?? "",
        UseSsl = bool.TryParse(s["UseSsl"], out var ssl) && ssl,
        FromAddress = s["FromAddress"]?.Trim() ?? "",
        FromName = string.IsNullOrWhiteSpace(s["FromName"]) ? "Crivo Gestão" : s["FromName"]!.Trim(),
    };

    /// <summary>Envio em segundo plano dos e-mails da fila. Hosts que enviam e-mail (API, worker) chamam isto.</summary>
    public static IServiceCollection AddEmailDispatcher(this IServiceCollection services) =>
        services.AddHostedService<EmailDispatcher>();
}
