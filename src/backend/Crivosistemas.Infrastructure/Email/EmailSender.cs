using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Crivosistemas.Infrastructure.Email;

public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>Verdadeiro para TLS implícito (porta 465). Caso contrário, usa STARTTLS quando o servidor oferece (587, relays locais).</summary>
    public bool UseSsl { get; set; }
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Crivo Gestão";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public sealed record EmailMessage(string To, string Subject, string Html, string Text);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 20_000 };
        var security = o.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
        await client.ConnectAsync(o.Host, o.Port, security, ct);
        if (!string.IsNullOrEmpty(o.Username)) await client.AuthenticateAsync(o.Username, o.Password, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>Usado quando o SMTP não está configurado: a mensagem vai para o log (o texto inclui links de uso único; só use em desenvolvimento).</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogWarning("SMTP não configurado; o e-mail \"{Subject}\" para {To} não foi enviado. Texto:\n{Text}",
            message.Subject, message.To, message.Text);
        return Task.CompletedTask;
    }
}
