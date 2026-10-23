using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Domain.Identity;
using Crivosistemas.Infrastructure.Email;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crivosistemas.Api.Modules.Auth;

public sealed record LoginRequest(
    [Required(ErrorMessage = "Informe seu e-mail.")] string Email,
    [Required(ErrorMessage = "Informe sua senha.")] string Password);

public sealed record ChangePasswordRequest(
    [Required(ErrorMessage = "Informe a senha atual.")] string CurrentPassword,
    [Required(ErrorMessage = "Informe a nova senha."), StringLength(128, MinimumLength = 8, ErrorMessage = "A senha deve ter pelo menos 8 caracteres.")] string NewPassword);

public sealed record ForgotPasswordRequest([Required] string Email);

public sealed record ResetPasswordRequest(
    [Required] string Token,
    [Required(ErrorMessage = "Informe uma senha."), StringLength(128, MinimumLength = 8, ErrorMessage = "A senha deve ter pelo menos 8 caracteres.")] string Password);

public sealed record MeResponse(Guid Id, string Name, string Email, string Role);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Não há cadastro público: o primeiro administrador nasce pela linha de comando (--seed-admin) e os demais são criados em Usuários.
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", Login).RequireRateLimiting("auth").AllowAnonymous();
        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
        group.MapGet("/me", Me).RequireAuthorization();
        group.MapPost("/change-password", ChangePassword).RequireAuthorization().RequireRateLimiting("auth");
        group.MapPost("/forgot-password", ForgotPassword).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/reset-password", ResetPassword).AllowAnonymous().RequireRateLimiting("auth");
    }

    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    internal static MeResponse BuildMe(User user) => new(user.Id, user.Name, user.Email, User.RoleName(user.Role));

    private static async Task<IResult> Login(LoginRequest req, AppDbContext db, IPasswordHasher<User> hasher, HttpContext http, CancellationToken ct)
    {
        var email = User.NormalizeEmail(req.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var result = user is null
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        // Mesma resposta para e-mail inexistente, senha errada e conta desativada: não revela quais contas existem.
        if (user is null || result == PasswordVerificationResult.Failed || !user.IsActive)
            return Results.Problem(statusCode: 401, title: "E-mail ou senha incorretos.");

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, req.Password);
        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await Session.SignInAsync(http, user);
        return Results.Ok(BuildMe(user));
    }

    private static async Task<IResult> Me(AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var userId = http.User.Id();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.Status == UserStatus.Active, ct);
        if (user is null)
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Unauthorized();
        }
        return Results.Ok(BuildMe(user));
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest req, AppDbContext db, IPasswordHasher<User> hasher, HttpContext http, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == http.User.Id(), ct);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword) == PasswordVerificationResult.Failed)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = ["Senha atual incorreta."] });
        user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
        db.Audit(http, "auth.password_changed", "User", user.Id);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Sempre 204, exista ou não o e-mail, para o endpoint não servir para descobrir contas.</summary>
    private static async Task<IResult> ForgotPassword(ForgotPasswordRequest req, AppDbContext db, EmailQueue emails, IOptions<AppOptions> app, HttpContext http, CancellationToken ct)
    {
        var email = User.NormalizeEmail(req.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.Status == UserStatus.Active, ct);
        if (user is not null)
        {
            var (token, hash) = SecureToken.Create();
            db.UserTokens.Add(new UserToken { UserId = user.Id, Purpose = UserTokenPurpose.ResetPassword, TokenHash = hash, ExpiresAt = DateTime.UtcNow.Add(ResetLifetime) });
            db.Audit(http, "auth.password_reset_requested", "User", user.Id);
            await db.SaveChangesAsync(ct);
            emails.Enqueue(EmailTemplates.ResetPassword(user.Email, user.Name, app.Value.Link($"/redefinir-senha?token={token}")));
        }
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPassword(ResetPasswordRequest req, AppDbContext db, IPasswordHasher<User> hasher, HttpContext http, CancellationToken ct)
    {
        var hash = SecureToken.Hash(req.Token.Trim());
        var now = DateTime.UtcNow;
        var token = await db.UserTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == UserTokenPurpose.ResetPassword, ct);
        if (token is null || !token.IsUsable(now))
            return Results.Problem(statusCode: 400, title: "Este link expirou ou já foi usado. Peça um novo.");
        var user = await db.Users.FirstAsync(u => u.Id == token.UserId, ct);
        if (!user.IsActive) return Results.Problem(statusCode: 400, title: "Esta conta está desativada.");
        user.PasswordHash = hasher.HashPassword(user, req.Password);
        await db.UserTokens.Where(t => t.UserId == user.Id && t.Purpose == UserTokenPurpose.ResetPassword && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        db.Audit(http, "auth.password_reset", "User", user.Id);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
