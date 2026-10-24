using System.Security.Cryptography;
using System.Text;
using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Identity;

public enum UserTokenPurpose { ResetPassword = 1 }

/// <summary>Token de uso único enviado por e-mail. Só o hash SHA-256 é gravado: um vazamento do banco não expõe links válidos.</summary>
public class UserToken : Entity
{
    public Guid UserId { get; set; }
    public UserTokenPurpose Purpose { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsUsable(DateTime now) => UsedAt is null && ExpiresAt > now;
}

public static class SecureToken
{
    /// <summary>Devolve um token aleatório seguro para URL e o hash a gravar.</summary>
    public static (string Token, string Hash) Create()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, Hash(token));
    }

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>Chave de acesso (passkey/WebAuthn) de um usuário. Só a chave pública fica no banco: um vazamento não permite entrar.</summary>
public class Passkey : Entity
{
    public Guid UserId { get; set; }
    public required byte[] CredentialId { get; set; }
    public required byte[] PublicKey { get; set; }
    /// <summary>Contador do autenticador; se uma assinatura vier com contador menor, a chave foi clonada.</summary>
    public long SignCount { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}
