using System.Security.Cryptography;
using System.Text;

namespace Crivosistemas.Domain.Integracoes;

public enum ResultadoAuth { Ok, NaoAutorizado, TimestampInvalido }

/// <summary>Autenticação da integração com o crivonft, nos dois sentidos: Bearer + HMAC-SHA256 de "{timestamp}.{corpo cru}".
/// Tudo em tempo constante; sem I/O. Veja docs/integracao-crivonft.md.</summary>
public static class CrivoAuth
{
    public const int JanelaSegundos = 300;
    public const string CabecalhoAssinatura = "X-Crivo-Signature";
    public const string CabecalhoTimestamp = "X-Crivo-Timestamp";

    /// <summary>"sha256=" + hex minúsculo do HMAC-SHA256(segredo, "{timestamp}.{corpo}"). O corpo entra com os bytes exatos recebidos.</summary>
    public static string Assinar(string segredo, string timestamp, ReadOnlySpan<byte> corpo)
    {
        var prefixo = Encoding.UTF8.GetBytes(timestamp + ".");
        var mensagem = new byte[prefixo.Length + corpo.Length];
        prefixo.CopyTo(mensagem, 0);
        corpo.CopyTo(mensagem.AsSpan(prefixo.Length));
        return "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), mensagem));
    }

    public static string Assinar(string segredo, string timestamp, string corpo) =>
        Assinar(segredo, timestamp, Encoding.UTF8.GetBytes(corpo));

    /// <summary>Compara dois textos em tempo constante (os dois lados passam por SHA-256, então o tamanho também não vaza).</summary>
    public static bool IguaisEmTempoConstante(string? a, string? b)
    {
        if (a is null || b is null) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
    }

    public static ResultadoAuth Verificar(string token, string segredo, string? authorization, string? timestamp, string? assinatura,
        ReadOnlySpan<byte> corpo, long agoraSegundos)
    {
        var bearer = authorization is not null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim() : null;
        var tokenOk = IguaisEmTempoConstante(bearer, token);

        var ts = timestamp?.Trim();
        var tsValido = long.TryParse(ts, out var segundos);
        // Calcula a assinatura mesmo com timestamp ruim, para o tempo de resposta não revelar qual parte falhou.
        var esperada = Assinar(segredo, ts ?? "", corpo);
        var assinaturaOk = IguaisEmTempoConstante(assinatura?.Trim().ToLowerInvariant(), esperada);

        if (!tokenOk || !assinaturaOk) return ResultadoAuth.NaoAutorizado;
        if (!tsValido || Math.Abs(agoraSegundos - segundos) > JanelaSegundos) return ResultadoAuth.TimestampInvalido;
        return ResultadoAuth.Ok;
    }
}
