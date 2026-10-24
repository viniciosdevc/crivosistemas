using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Integracoes;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Crivosistemas.Api.Modules.Manutencao;

/// <summary>Modo manutenção da loja: o estado vive no crivonft; aqui só se chama a rota assinada dele.</summary>
public static class ManutencaoEndpoints
{
    private const string Caminho = "/api/integracoes/gestao/manutencao";

    public static void MapManutencaoEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/manutencao").WithTags("Manutenção").RequireAuthorization(Session.AdminPolicy);
        g.MapGet("", Estado).WithSummary("Estado atual do modo manutenção da loja (crivonft).");
        g.MapPut("", Alterar).WithSummary("Liga ou desliga o modo manutenção da loja (crivonft).");
    }

    public record AlterarManutencaoRequest(bool Ativo, [property: MaxLength(300, ErrorMessage = "A mensagem pode ter no máximo 300 caracteres.")] string? Mensagem);

    private static async Task<IResult> Estado(IOptions<CrivonftOptions> o, ICrivonftClient crivonft, CancellationToken ct) =>
        (await Chamar(o, crivonft, HttpMethod.Get, null, ct)).Resultado;

    private static async Task<IResult> Alterar([FromBody] AlterarManutencaoRequest req, HttpContext http, AppDbContext db, IOptions<CrivonftOptions> o, ICrivonftClient crivonft, CancellationToken ct)
    {
        var (sucesso, resultado) = await Chamar(o, crivonft, HttpMethod.Put, new { ativo = req.Ativo, mensagem = req.Mensagem }, ct);
        if (sucesso)
        {
            db.Audit(http, req.Ativo ? "manutencao.ligada" : "manutencao.desligada", "Manutencao", null, new { req.Mensagem });
            await db.SaveChangesAsync(ct);
        }
        return resultado;
    }

    private static async Task<(bool Sucesso, IResult Resultado)> Chamar(IOptions<CrivonftOptions> o, ICrivonftClient crivonft, HttpMethod metodo, object? corpo, CancellationToken ct)
    {
        if (!o.Value.Envia) return (false, Results.Json(new { title = "A integração com o crivonft não está configurada.", code = "integracao_nao_configurada", status = 503 }, statusCode: 503));
        try
        {
            var r = await crivonft.ChamarGestaoAsync(metodo, Caminho, corpo, ct);
            if (r.Sucesso) return (true, Results.Content(string.IsNullOrWhiteSpace(r.Corpo) ? "{}" : r.Corpo, "application/json"));
            return (false, IntegracoesEndpoints.Crivonft502(new CrivonftException($"HTTP {r.StatusHttp}: {(r.Corpo.Length > 200 ? r.Corpo[..200] : r.Corpo)}", r.StatusHttp)));
        }
        catch (CrivonftException e)
        {
            return (false, IntegracoesEndpoints.Crivonft502(e));
        }
    }
}
