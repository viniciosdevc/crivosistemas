using System.Net;
using System.Text;

namespace Crivosistemas.Infrastructure.Email;

/// <summary>HTML simples, em tabelas, que aparece igual no Gmail, Outlook e Apple Mail.
/// Uma coluna, fontes do sistema, texto preto sobre branco, um único botão. Sem imagens nem emojis.</summary>
public static class EmailTemplates
{
    public static EmailMessage ResetPassword(string to, string name, string url) => Build(to,
        "Redefinir sua senha",
        $"Olá, {FirstName(name)}.",
        ["Recebemos um pedido para redefinir a senha da sua conta. Clique no botão abaixo para escolher uma nova."],
        ("Redefinir senha", url),
        "O link vale por 1 hora e só pode ser usado uma vez. Se não foi você, sua senha continua a mesma.");

    private static string FirstName(string name) => name.Trim().Split(' ', 2)[0];

    private static EmailMessage Build(string to, string subject, string greeting, IReadOnlyList<string> paragraphs,
        (string Label, string Url)? button, string footnote)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        const string font = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

        var body = new StringBuilder();
        foreach (var p in paragraphs)
            body.Append($"<p style=\"margin:0 0 16px;font-size:15px;line-height:24px;color:#18181b;\">{E(p)}</p>");
        if (button is { } b)
            body.Append($"""
                <table role="presentation" cellpadding="0" cellspacing="0" style="margin:8px 0 24px;"><tr>
                <td style="background:#18181b;border-radius:8px;">
                <a href="{E(b.Url)}" style="display:inline-block;padding:12px 20px;font-family:{font};font-size:14px;font-weight:600;color:#ffffff;text-decoration:none;">{E(b.Label)}</a>
                </td></tr></table>
                <p style="margin:0 0 16px;font-size:13px;line-height:20px;color:#71717a;">Se o botão não funcionar, copie e cole este endereço no navegador:<br><a href="{E(b.Url)}" style="color:#71717a;word-break:break-all;">{E(b.Url)}</a></p>
                """);

        var html = $"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(subject)}</title></head>
            <body style="margin:0;padding:0;background:#ffffff;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#ffffff;"><tr><td align="center" style="padding:40px 20px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;font-family:{font};">
            <tr><td style="padding:0 0 32px;font-size:15px;font-weight:700;color:#18181b;letter-spacing:-0.2px;">Crivo Gestão</td></tr>
            <tr><td>
            <h1 style="margin:0 0 20px;font-size:20px;line-height:28px;font-weight:600;color:#18181b;">{E(subject)}</h1>
            <p style="margin:0 0 16px;font-size:15px;line-height:24px;color:#18181b;">{E(greeting)}</p>
            {body}
            </td></tr>
            <tr><td style="padding:24px 0 0;border-top:1px solid #e4e4e7;font-size:12px;line-height:18px;color:#a1a1aa;">{E(footnote)}</td></tr>
            </table></td></tr></table>
            </body></html>
            """;

        var text = new StringBuilder().AppendLine(subject).AppendLine().AppendLine(greeting).AppendLine();
        foreach (var p in paragraphs) text.AppendLine(p).AppendLine();
        if (button is { } t) text.AppendLine($"{t.Label}: {t.Url}").AppendLine();
        text.AppendLine(footnote);
        return new EmailMessage(to, subject, html, text.ToString());
    }
}
