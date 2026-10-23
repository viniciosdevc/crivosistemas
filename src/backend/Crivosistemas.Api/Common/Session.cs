using System.Security.Claims;
using Crivosistemas.Domain.Identity;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Common;

public static class Session
{
    public const string AdminRole = "admin";
    public const string AdminPolicy = "admin";

    public static ClaimsPrincipal Principal(User user) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Name),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, User.RoleName(user.Role)),
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    public static Task SignInAsync(HttpContext http, User user) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Principal(user), new AuthenticationProperties { IsPersistent = true });

    /// <summary>A cada requisição confere no banco se a pessoa continua ativa e relê o papel: desativar um usuário ou trocar
    /// seu papel vale imediatamente, sem esperar o cookie expirar. É uma busca por chave primária, barata para uma empresa só.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, context.HttpContext.RequestAborted);
        if (user is null || user.Status != UserStatus.Active)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        context.ReplacePrincipal(Principal(user));
    }
}
