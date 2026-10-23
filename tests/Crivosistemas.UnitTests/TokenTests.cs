using Crivosistemas.Domain.Identity;

namespace Crivosistemas.UnitTests;

public class TokenTests
{
    [Fact]
    public void Token_e_hash_sao_consistentes_e_aleatorios()
    {
        var (t1, h1) = SecureToken.Create();
        var (t2, _) = SecureToken.Create();
        Assert.NotEqual(t1, t2);
        Assert.Equal(h1, SecureToken.Hash(t1));
        Assert.Equal(64, h1.Length);
        Assert.DoesNotContain('=', t1);
        Assert.DoesNotContain('+', t1);
        Assert.DoesNotContain('/', t1);
    }

    [Fact]
    public void Token_vencido_ou_usado_nao_vale()
    {
        var now = DateTime.UtcNow;
        var ok = new UserToken { TokenHash = "x", ExpiresAt = now.AddMinutes(5) };
        var vencido = new UserToken { TokenHash = "x", ExpiresAt = now.AddMinutes(-1) };
        var usado = new UserToken { TokenHash = "x", ExpiresAt = now.AddMinutes(5), UsedAt = now };
        Assert.True(ok.IsUsable(now));
        Assert.False(vencido.IsUsable(now));
        Assert.False(usado.IsUsable(now));
    }
}
