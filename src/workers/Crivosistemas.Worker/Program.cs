using Crivosistemas.Infrastructure;
using Crivosistemas.Worker;
using Microsoft.AspNetCore.DataProtection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
// O worker compartilha o anel de chaves da API para poder ler o que ela protege (e usa o mesmo prefixo do Redis).
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("crivosistemas");
if (builder.Configuration.GetConnectionString("Redis") is { Length: > 0 } redisConnection)
    dataProtection.PersistKeysToStackExchangeRedis(StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection + ",abortConnect=false"), DependencyInjection.RedisPrefix + "dataprotection-keys");
builder.Services.AddHostedService<TokenCleanupJob>();
builder.Services.AddEmailDispatcher();
builder.Services.AddHostedService<ReconciliacaoJob>();
builder.Services.AddHostedService<PrecificacaoEnvioJob>();

builder.Build().Run();
