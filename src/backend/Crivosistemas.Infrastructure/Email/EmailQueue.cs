using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crivosistemas.Infrastructure.Email;

/// <summary>Desacopla o envio da requisição: os endpoints enfileiram e retornam; um laço em segundo plano entrega com novas tentativas.
/// As mensagens só carregam links de uso único, então perder uma num reinício custa um "reenviar", não dados.</summary>
public sealed class EmailQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);

    internal ChannelReader<EmailMessage> Reader => _channel.Reader;
}

public sealed class EmailDispatcher(EmailQueue queue, IServiceScopeFactory scopes, ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, stoppingToken);
                    break;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "Falha ao enviar o e-mail \"{Subject}\" (tentativa {Attempt})", message.Subject, attempt);
                    if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(5 * attempt), stoppingToken);
                }
            }
        }
    }
}
