using Crivosistemas.Infrastructure.Integracoes;

namespace Crivosistemas.Worker;

/// <summary>Envia ao crivonft as vendas manuais da fila (PUT assinado), com backoff entre tentativas. Sem a integração configurada, não envia.</summary>
public sealed class VendaEnvioJob(IServiceScopeFactory scopes, ICrivonftClient crivonft, IConfiguration config, ILogger<VendaEnvioJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var segundos = int.TryParse(config["Worker:VendaEnvioSegundos"], out var s) && s > 0 ? s : 30;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(segundos));
        do
        {
            if (!crivonft.EnvioConfigurado) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var servico = scope.ServiceProvider.GetRequiredService<VendaEnvioService>();
                if (await servico.ProcessarAsync(stoppingToken) is var n and > 0) logger.LogInformation("{N} venda(s) manual(is) enviada(s) ao crivonft", n);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Falha no ciclo de envio das vendas; tenta de novo no próximo");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
