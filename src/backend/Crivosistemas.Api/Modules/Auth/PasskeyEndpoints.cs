using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Crivosistemas.Api.Common;
using Crivosistemas.Domain.Identity;
using Crivosistemas.Infrastructure.Persistence;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Crivosistemas.Api.Modules.Auth;

public sealed record PasskeyRegisterRequest(
    [Required(ErrorMessage = "Dê um nome para esta chave.")] [StringLength(80, ErrorMessage = "Use até 80 caracteres.")] string Name,
    [Required] JsonElement Credential);

public sealed record PasskeyLoginRequest([Required] string ChallengeId, [Required] JsonElement Credential);

public sealed record PasskeyLoginOptionsResponse(string ChallengeId, AssertionOptions Options);

public sealed record PasskeyResponse(Guid Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);

/// <summary>Login com passkey (WebAuthn). A senha continua valendo; a passkey é uma alternativa mais forte porque a verificação
/// do usuário (biometria/PIN) e a posse do dispositivo já são dois fatores, e a chave só funciona no domínio do painel.
/// O login usa credenciais descobertas (sem digitar e-mail), então o endpoint também não revela quais contas existem.</summary>
public static class PasskeyEndpoints
{
    private static readonly DistributedCacheEntryOptions ChallengeLifetime = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3) };
    private const int MaxPasskeysPerUser = 10;

    public static void MapPasskeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapGet("/passkeys", List).RequireAuthorization();
        group.MapPost("/passkeys/options", RegisterOptions).RequireAuthorization().RequireRateLimiting("auth");
        group.MapPost("/passkeys", Register).RequireAuthorization().RequireRateLimiting("auth");
        group.MapDelete("/passkeys/{id:guid}", Remove).RequireAuthorization();

        group.MapPost("/passkey-login/options", LoginOptions).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/passkey-login", Login).AllowAnonymous().RequireRateLimiting("auth");
    }

    private static string RegisterKey(Guid userId) => $"passkey:register:{userId:N}";
    private static string LoginKey(string challengeId) => $"passkey:login:{challengeId}";

    private static async Task<IResult> List(AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var userId = http.User.Id();
        var items = await db.Passkeys.AsNoTracking().Where(p => p.UserId == userId).OrderBy(p => p.CreatedAt)
            .Select(p => new PasskeyResponse(p.Id, p.Name, p.CreatedAt, p.LastUsedAt)).ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> RegisterOptions(IFido2 fido2, IDistributedCache cache, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == http.User.Id(), ct);
        var existing = await db.Passkeys.AsNoTracking().Where(p => p.UserId == user.Id).Select(p => p.CredentialId).ToListAsync(ct);
        if (existing.Count >= MaxPasskeysPerUser)
            return Results.Problem(statusCode: 422, title: $"Você já tem {MaxPasskeysPerUser} chaves. Remova uma para adicionar outra.", extensions: new Dictionary<string, object?> { ["code"] = "passkey.limit" });

        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = user.Id.ToByteArray(), Name = user.Email, DisplayName = user.Name },
            ExcludeCredentials = existing.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                // Passkey de verdade: guardada no dispositivo/gerenciador (descobrível) e liberada só com biometria ou PIN.
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });
        await cache.SetStringAsync(RegisterKey(user.Id), options.ToJson(), ChallengeLifetime, ct);
        return Results.Ok(options);
    }

    private static async Task<IResult> Register(PasskeyRegisterRequest req, IFido2 fido2, IDistributedCache cache, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var userId = http.User.Id();
        var key = RegisterKey(userId);
        var stored = await cache.GetStringAsync(key, ct);
        if (stored is null) return Results.Problem(statusCode: 400, title: "O pedido expirou. Tente adicionar a chave de novo.");
        await cache.RemoveAsync(key, ct); // challenge de uso único

        AuthenticatorAttestationRawResponse? attestation;
        try { attestation = req.Credential.Deserialize<AuthenticatorAttestationRawResponse>(); }
        catch (JsonException) { attestation = null; }
        if (attestation is null) return Results.Problem(statusCode: 400, title: "Resposta da chave inválida.");

        RegisteredPublicKeyCredential credential;
        try
        {
            credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestation,
                OriginalOptions = CredentialCreateOptions.FromJson(stored),
                IsCredentialIdUniqueToUserCallback = async (args, token) =>
                    !await db.Passkeys.AnyAsync(p => p.CredentialId == args.CredentialId, token),
            }, ct);
        }
        catch (Fido2VerificationException)
        {
            return Results.Problem(statusCode: 400, title: "Não foi possível validar a chave. Tente novamente.");
        }

        var passkey = new Passkey
        {
            UserId = userId,
            CredentialId = credential.Id,
            PublicKey = credential.PublicKey,
            SignCount = credential.SignCount,
            Name = req.Name.Trim(),
        };
        db.Passkeys.Add(passkey);
        db.Audit(http, "auth.passkey_added", "Passkey", passkey.Id, new { passkey.Name });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new PasskeyResponse(passkey.Id, passkey.Name, passkey.CreatedAt, null));
    }

    private static async Task<IResult> Remove(Guid id, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var passkey = await db.Passkeys.FirstOrDefaultAsync(p => p.Id == id && p.UserId == http.User.Id(), ct);
        if (passkey is null) throw new NotFoundException("Chave não encontrada.");
        db.Passkeys.Remove(passkey);
        db.Audit(http, "auth.passkey_removed", "Passkey", passkey.Id, new { passkey.Name });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> LoginOptions(IFido2 fido2, IDistributedCache cache, CancellationToken ct)
    {
        // Sem lista de credenciais permitidas: o navegador mostra as passkeys do site e a própria chave diz quem é o usuário.
        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required,
        });
        var challengeId = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        await cache.SetStringAsync(LoginKey(challengeId), options.ToJson(), ChallengeLifetime, ct);
        return Results.Ok(new PasskeyLoginOptionsResponse(challengeId, options));
    }

    private static async Task<IResult> Login(PasskeyLoginRequest req, IFido2 fido2, IDistributedCache cache, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        IResult Fail() => Results.Problem(statusCode: 401, title: "Não foi possível entrar com a chave de acesso.");

        var key = LoginKey(req.ChallengeId);
        var stored = await cache.GetStringAsync(key, ct);
        if (stored is null) return Fail();
        await cache.RemoveAsync(key, ct); // challenge de uso único: cada tentativa gasta o seu

        AuthenticatorAssertionRawResponse? assertion;
        try { assertion = req.Credential.Deserialize<AuthenticatorAssertionRawResponse>(); }
        catch (JsonException) { assertion = null; }
        if (assertion is null) return Fail();

        var passkey = await db.Passkeys.FirstOrDefaultAsync(p => p.CredentialId == assertion.RawId, ct);
        if (passkey is null) return Fail();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == passkey.UserId, ct);
        if (user is null || !user.IsActive) return Fail();

        VerifyAssertionResult result;
        try
        {
            result = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = assertion,
                OriginalOptions = AssertionOptions.FromJson(stored),
                StoredPublicKey = passkey.PublicKey,
                StoredSignatureCounter = (uint)passkey.SignCount,
                // O userHandle que o autenticador devolve precisa ser o do dono da chave.
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                    Task.FromResult(args.UserHandle is { Length: 16 } && new Guid(args.UserHandle) == passkey.UserId),
            }, ct);
        }
        catch (Fido2VerificationException)
        {
            return Fail();
        }

        passkey.SignCount = result.SignCount;
        passkey.LastUsedAt = DateTime.UtcNow;
        user.LastLoginAt = DateTime.UtcNow;
        db.Audit(http, "auth.passkey_login", "User", user.Id, new { passkey = passkey.Name });
        await db.SaveChangesAsync(ct);

        await Session.SignInAsync(http, user);
        return Results.Ok(AuthEndpoints.BuildMe(user));
    }
}
