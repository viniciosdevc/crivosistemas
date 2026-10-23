using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Worker;

/// <summary>Manutenção: apaga tokens de redefinição de senha vencidos ou já usados. É também o esqueleto dos próximos jobs
/// (a integração com o crivonft roda em ReconciliacaoJob e PrecificacaoEnvioJob).</summary>
public sealed class TokenCleanupJob(IServiceScopeFactory scopes, IConfiguration config, ILogger<TokenCleanupJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = int.TryParse(config["Worker:TokenCleanupMinutes"], out var m) && m > 0 ? m : 360;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                var removed = await db.UserTokens.Where(t => t.ExpiresAt < now || t.UsedAt != null).ExecuteDeleteAsync(stoppingToken);
                if (removed > 0) logger.LogInformation("Removidos {Count} token(s) de senha vencidos ou usados", removed);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Falha na limpeza de tokens; tenta de novo no próximo ciclo");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
