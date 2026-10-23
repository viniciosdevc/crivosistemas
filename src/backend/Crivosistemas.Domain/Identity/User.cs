using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Identity;

public enum UserStatus { Active = 1, Disabled = 2 }

/// <summary>Admin gerencia usuários e configurações; operador usa o dia a dia (vendas, clientes) sem administrar o sistema.</summary>
public enum UserRole { Admin = 1, Operator = 2 }

public class User : Entity
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Operator;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    public bool IsAdmin => Role == UserRole.Admin;
    public bool IsActive => Status == UserStatus.Active;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Nome do papel como trafega na API (camelCase).</summary>
    public static string RoleName(UserRole role) => role == UserRole.Admin ? "admin" : "operator";

    public static UserRole ParseRole(string role) => role.Trim().ToLowerInvariant() switch
    {
        "admin" => UserRole.Admin,
        "operator" or "operador" => UserRole.Operator,
        _ => throw new DomainException("users.invalid_role", "Papel inválido. Use admin ou operador."),
    };

    /// <summary>Garante que o sistema nunca fique sem um administrador ativo.</summary>
    public static void EnsureAdminRemains(User target, int otherActiveAdmins, string actionDescription)
    {
        if (target.IsAdmin && target.IsActive && otherActiveAdmins == 0)
            throw new DomainException("users.last_admin", $"Não é possível {actionDescription}: o sistema precisa de pelo menos um administrador ativo.");
    }
}
