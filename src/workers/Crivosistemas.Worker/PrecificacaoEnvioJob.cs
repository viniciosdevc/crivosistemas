using Crivosistemas.Infrastructure.Integracoes;

namespace Crivosistemas.Worker;

/// <summary>Envia ao crivonft as regras de precificação do outbox (PUT assinado), com backoff entre tentativas.
/// Sem a integração configurada, não envia.</summary>
public sealed class PrecificacaoEnvioJob(IServiceScopeFactory scopes, ICrivonftClient crivonft, IConfiguration config, ILogger<PrecificacaoEnvioJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var segundos = int.TryParse(config["Worker:PrecificacaoEnvioSegundos"], out var s) && s > 0 ? s : 30;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(segundos));
        do
        {
            if (!crivonft.EnvioConfigurado) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var servico = scope.ServiceProvider.GetRequiredService<PrecificacaoEnvioService>();
                if (await servico.ProcessarAsync(stoppingToken) > 0) logger.LogInformation("Regras de precificação enviadas ao crivonft");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Falha no ciclo de envio da precificação; tenta de novo no próximo");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
