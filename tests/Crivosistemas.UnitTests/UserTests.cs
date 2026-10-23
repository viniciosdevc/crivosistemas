using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Identity;

namespace Crivosistemas.UnitTests;

public class UserTests
{
    private static User Admin(UserStatus status = UserStatus.Active) =>
        new() { Name = "Ana", Email = "ana@teste.dev", Role = UserRole.Admin, Status = status };

    [Theory]
    [InlineData("  Ana@Teste.DEV ", "ana@teste.dev")]
    [InlineData("a@b.com", "a@b.com")]
    public void Normaliza_email(string input, string expected) => Assert.Equal(expected, User.NormalizeEmail(input));

    [Theory]
    [InlineData("admin", UserRole.Admin)]
    [InlineData(" ADMIN ", UserRole.Admin)]
    [InlineData("operator", UserRole.Operator)]
    [InlineData("operador", UserRole.Operator)]
    public void Interpreta_papel(string input, UserRole expected) => Assert.Equal(expected, User.ParseRole(input));

    [Fact]
    public void Papel_invalido_gera_erro_de_dominio()
    {
        var e = Assert.Throws<DomainException>(() => User.ParseRole("dono"));
        Assert.Equal("users.invalid_role", e.Code);
    }

    [Fact]
    public void Nomes_de_papel_sao_estaveis()
    {
        Assert.Equal("admin", User.RoleName(UserRole.Admin));
        Assert.Equal("operator", User.RoleName(UserRole.Operator));
    }

    [Fact]
    public void Ultimo_admin_ativo_nao_pode_sair()
    {
        var e = Assert.Throws<DomainException>(() => User.EnsureAdminRemains(Admin(), otherActiveAdmins: 0, "desativar este usuário"));
        Assert.Equal("users.last_admin", e.Code);
    }

    [Fact]
    public void Admin_pode_sair_se_houver_outro()
    {
        User.EnsureAdminRemains(Admin(), otherActiveAdmins: 1, "desativar este usuário");
    }

    [Fact]
    public void Operador_ou_admin_ja_desativado_nao_afeta_a_regra()
    {
        User.EnsureAdminRemains(new User { Name = "Op", Email = "op@teste.dev", Role = UserRole.Operator }, 0, "desativar");
        User.EnsureAdminRemains(Admin(UserStatus.Disabled), 0, "desativar");
    }

    [Fact]
    public void Novo_usuario_nasce_operador_e_ativo()
    {
        var u = new User { Name = "Bia", Email = "bia@teste.dev" };
        Assert.Equal(UserRole.Operator, u.Role);
        Assert.True(u.IsActive);
        Assert.False(u.IsAdmin);
    }
}
