using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Integracoes;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;

namespace Crivosistemas.Api.Modules.Convites;

public sealed record CriarLinkRequest([StringLength(200, ErrorMessage = "A nota pode ter até 200 caracteres.")] string? Nota);
public sealed record ConvidarNickRequest(
    [Required(ErrorMessage = "Informe o nick."), StringLength(80, ErrorMessage = "O nick pode ter até 80 caracteres.")] string Nickname,
    [StringLength(200, ErrorMessage = "A nota pode ter até 200 caracteres.")] string? Nota);

/// <summary>Convites da loja (crivonft): links de acesso de convidado e nicks Habbo liberados. Os dados vivem no crivonft; aqui só se chama a rota assinada dele.</summary>
public static class ConvitesEndpoints
{
    public static void MapConvitesEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/convites").WithTags("Convites").RequireAuthorization(Session.AdminPolicy);
        g.MapGet("", Listar).WithSummary("Links e nicks convidados na loja, direto do crivonft.");
        g.MapPost("/links", CriarLink).WithSummary("Gera um link de acesso de convidado. A URL só vem nesta resposta.");
        g.MapPost("/nicks", ConvidarNick).WithSummary("Libera um nick Habbo para entrar na loja.");
        g.MapDelete("/links/{id:guid}", (Guid id, HttpContext http, AppDbContext db, ICrivonftClient c, CancellationToken ct) => Revogar("revogar_link", id, http, db, c, ct))
            .WithSummary("Revoga um link de convidado.");
        g.MapDelete("/nicks/{id:guid}", (Guid id, HttpContext http, AppDbContext db, ICrivonftClient c, CancellationToken ct) => Revogar("revogar_nick", id, http, db, c, ct))
            .WithSummary("Revoga o convite de um nick.");
    }

    private static async Task<(bool Ok, IResult Resultado)> Executar(object? corpo, ICrivonftClient c, CancellationToken ct)
    {
        try
        {
            var json = await c.ConvitesAsync(corpo is null ? null : JsonSerializer.Serialize(corpo), ct);
            return (true, Results.Content(json, "application/json"));
        }
        catch (CrivonftException e)
        {
            return (false, IntegracoesEndpoints.Crivonft502(e));
        }
    }

    private static async Task<IResult> Listar(ICrivonftClient c, CancellationToken ct) => (await Executar(null, c, ct)).Resultado;

    private static async Task<IResult> CriarLink(CriarLinkRequest? req, HttpContext http, AppDbContext db, ICrivonftClient c, CancellationToken ct)
    {
        var (ok, r) = await Executar(new { acao = "criar_link", nota = Nota(req?.Nota, http) }, c, ct);
        if (ok)
        {
            db.Audit(http, "convites.link_criado", "ConviteLoja", null, new { nota = req?.Nota });
            await db.SaveChangesAsync(ct);
        }
        return r;
    }

    private static async Task<IResult> ConvidarNick(ConvidarNickRequest req, HttpContext http, AppDbContext db, ICrivonftClient c, CancellationToken ct)
    {
        var (ok, r) = await Executar(new { acao = "convidar_nick", nickname = req.Nickname.Trim(), nota = Nota(req.Nota, http) }, c, ct);
        if (ok)
        {
            db.Audit(http, "convites.nick_convidado", "ConviteLoja", null, new { req.Nickname });
            await db.SaveChangesAsync(ct);
        }
        return r;
    }

    private static async Task<IResult> Revogar(string acao, Guid id, HttpContext http, AppDbContext db, ICrivonftClient c, CancellationToken ct)
    {
        var (ok, r) = await Executar(new { acao, id }, c, ct);
        if (ok)
        {
            db.Audit(http, "convites." + acao, "ConviteLoja", id, null);
            await db.SaveChangesAsync(ct);
        }
        return r;
    }

    /// <summary>A gestão não é um usuário do /adm do crivonft; quem convidou vai na nota.</summary>
    private static string Nota(string? nota, HttpContext http)
    {
        var quem = http.User.Identity?.Name ?? "gestão";
        var t = string.IsNullOrWhiteSpace(nota) ? $"por {quem}" : $"{nota.Trim()} (por {quem})";
        return t.Length <= 200 ? t : t[..200];
    }
}
