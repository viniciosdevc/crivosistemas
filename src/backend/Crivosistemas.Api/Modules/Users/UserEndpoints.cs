using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Identity;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Users;

public sealed record CreateUserRequest(
    [Required(ErrorMessage = "Informe o nome."), StringLength(120, MinimumLength = 2, ErrorMessage = "Informe o nome.")] string Name,
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido."), StringLength(254)] string Email,
    [Required(ErrorMessage = "Informe uma senha."), StringLength(128, MinimumLength = 8, ErrorMessage = "A senha deve ter pelo menos 8 caracteres.")] string Password,
    [Required(ErrorMessage = "Informe o papel.")] string Role);

public sealed record UpdateUserRequest(
    [Required(ErrorMessage = "Informe o nome."), StringLength(120, MinimumLength = 2, ErrorMessage = "Informe o nome.")] string Name,
    [Required(ErrorMessage = "Informe o papel.")] string Role);

public sealed record UserDto(Guid Id, string Name, string Email, string Role, string Status, DateTime CreatedAt, DateTime? LastLoginAt, bool IsYou);

/// <summary>Usuários e acessos. Tudo aqui é exclusivo de administradores.</summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(Session.AdminPolicy);
        group.MapGet("", List);
        group.MapPost("", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapPost("/{id:guid}/deactivate", Deactivate);
        group.MapPost("/{id:guid}/activate", Activate);
    }

    private static string StatusName(UserStatus s) => s == UserStatus.Active ? "active" : "disabled";

    private static UserDto ToDto(User u, Guid me) =>
        new(u.Id, u.Name, u.Email, User.RoleName(u.Role), StatusName(u.Status), u.CreatedAt, u.LastLoginAt, u.Id == me);

    private static Task<int> OtherActiveAdmins(AppDbContext db, Guid exceptId, CancellationToken ct) =>
        db.Users.CountAsync(u => u.Id != exceptId && u.Role == UserRole.Admin && u.Status == UserStatus.Active, ct);

    private static async Task<IResult> List(AppDbContext db, HttpContext http, string? search, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Name.ToLower().Contains(term) || u.Email.Contains(term));
        }
        if (status is "active") query = query.Where(u => u.Status == UserStatus.Active);
        else if (status is "disabled") query = query.Where(u => u.Status == UserStatus.Disabled);

        var me = http.User.Id();
        var result = await query.OrderBy(u => u.Name).ThenBy(u => u.Id).ToPageAsync(page, pageSize, ct);
        return Results.Ok(new PagedResult<UserDto>(result.Items.Select(u => ToDto(u, me)).ToList(), result.Page, result.PageSize, result.Total));
    }

    private static async Task<IResult> Create(CreateUserRequest req, AppDbContext db, IPasswordHasher<User> hasher, HttpContext http, CancellationToken ct)
    {
        var role = User.ParseRole(req.Role);
        var email = User.NormalizeEmail(req.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Este e-mail já está cadastrado."] });

        var user = new User { Name = req.Name.Trim(), Email = email, Role = role };
        user.PasswordHash = hasher.HashPassword(user, req.Password);
        db.Users.Add(user);
        db.Audit(http, "users.created", "User", user.Id, new { email, role = User.RoleName(role) });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Este e-mail já está cadastrado."] });
        }
        return Results.Created($"/api/users/{user.Id}", ToDto(user, http.User.Id()));
    }

    private static async Task<IResult> Update(Guid id, UpdateUserRequest req, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Usuário não encontrado.");
        var role = User.ParseRole(req.Role);
        if (role != user.Role)
            User.EnsureAdminRemains(user, await OtherActiveAdmins(db, id, ct), "remover o papel de administrador");
        var changed = role != user.Role;
        user.Name = req.Name.Trim();
        user.Role = role;
        db.Audit(http, changed ? "users.role_changed" : "users.updated", "User", user.Id, new { role = User.RoleName(role) });
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(user, http.User.Id()));
    }

    private static async Task<IResult> Deactivate(Guid id, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Usuário não encontrado.");
        if (id == http.User.Id())
            throw new DomainException("users.self_deactivate", "Você não pode desativar o próprio acesso.");
        User.EnsureAdminRemains(user, await OtherActiveAdmins(db, id, ct), "desativar este usuário");
        if (user.IsActive)
        {
            user.Status = UserStatus.Disabled;
            db.Audit(http, "users.deactivated", "User", user.Id, new { user.Email });
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(ToDto(user, http.User.Id()));
    }

    private static async Task<IResult> Activate(Guid id, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Usuário não encontrado.");
        if (!user.IsActive)
        {
            user.Status = UserStatus.Active;
            db.Audit(http, "users.activated", "User", user.Id, new { user.Email });
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(ToDto(user, http.User.Id()));
    }
}
