using Crivosistemas.Infrastructure.Integracoes;

namespace Crivosistemas.Worker;

/// <summary>Reconciliação com o crivonft: agenda a rodada noturna (últimos 3 dias) e executa os pedidos pendentes (manuais).
/// Sem a integração configurada, não faz nada.</summary>
public sealed class ReconciliacaoJob(IServiceScopeFactory scopes, ICrivonftClient crivonft, IConfiguration config, ILogger<ReconciliacaoJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var segundos = int.TryParse(config["Worker:ReconciliacaoSegundos"], out var s) && s > 0 ? s : 30;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(segundos));
        do
        {
            if (!crivonft.ExportConfigurado && !crivonft.EnvioConfigurado) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var servico = scope.ServiceProvider.GetRequiredService<ReconciliacaoService>();
                await servico.AgendarNoturnaSeNecessarioAsync(DateTime.UtcNow, stoppingToken);
                while (await servico.ReivindicarPendenteAsync(stoppingToken) is { } exec)
                {
                    await servico.ExecutarAsync(exec, stoppingToken);
                    logger.LogInformation("Reconciliação {Tipo} {Estado}: {Vendas} vendas lidas, {Aplicadas} aplicadas", exec.Tipo, exec.Estado, exec.VendasLidas, exec.VendasAplicadas);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Falha no ciclo de reconciliação; tenta de novo no próximo");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
