using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Crivosistemas.Infrastructure.Email;

namespace Crivosistemas.IntegrationTests;

public sealed partial class CapturingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Espera o dispatcher em segundo plano e devolve o token do último link enviado a um endereço.</summary>
    public async Task<string> TokenFor(string to, string path)
    {
        for (var i = 0; i < 50; i++)
        {
            var match = Sent.Reverse().Where(m => m.To == to).Select(m => TokenRegex().Match(m.Text))
                .FirstOrDefault(m => m.Success && m.Value.Contains(path));
            if (match is not null) return match.Groups[1].Value;
            await Task.Delay(100);
        }
        throw new InvalidOperationException($"Nenhum e-mail com {path} enviado para {to}");
    }

    [GeneratedRegex(@"https?://\S+\?token=([A-Za-z0-9_-]+)")]
    private static partial Regex TokenRegex();
}
