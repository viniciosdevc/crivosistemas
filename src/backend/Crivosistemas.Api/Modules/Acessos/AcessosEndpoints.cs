using System.Globalization;
using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Integracoes;
using Crivosistemas.Infrastructure.Integracoes;
using Microsoft.Extensions.Options;

namespace Crivosistemas.Api.Modules.Acessos;

/// <summary>Acessos à loja por localização e ranking de clientes. Os dados vivem no crivonft (só agregados, sem IP); aqui só se chama a rota assinada dele.</summary>
public static class AcessosEndpoints
{
    public static void MapAcessosEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/acessos").WithTags("Acessos").RequireAuthorization(Session.AdminPolicy);
        g.MapGet("", Resumo).WithSummary("Acessos por país/estado/cidade, por dia, páginas e itens mais vistos e clientes mais acessados (pais opcional filtra páginas e itens), direto do crivonft.");
    }

    internal static int NormalizarDias(int? dias) => Math.Clamp(dias ?? 30, 1, 365);

    private static async Task<IResult> Resumo(int? dias, string? pais, IOptions<CrivonftOptions> o, ICrivonftClient crivonft, CancellationToken ct)
    {
        if (!o.Value.Envia) return Results.Json(new { title = "A integração com o crivonft não está configurada.", code = "integracao_nao_configurada", status = 503 }, statusCode: 503);
        try
        {
            var caminho = "/api/integracoes/gestao/acessos?dias=" + NormalizarDias(dias).ToString(CultureInfo.InvariantCulture);
            if (pais is { Length: 2 } && pais.All(char.IsAsciiLetter)) caminho += "&pais=" + pais.ToUpperInvariant();
            var r = await crivonft.ChamarGestaoAsync(HttpMethod.Get, caminho, null, ct);
            if (r.Sucesso) return Results.Content(string.IsNullOrWhiteSpace(r.Corpo) ? "{}" : r.Corpo, "application/json");
            return IntegracoesEndpoints.Crivonft502(new CrivonftException($"HTTP {r.StatusHttp}: {(r.Corpo.Length > 200 ? r.Corpo[..200] : r.Corpo)}", r.StatusHttp));
        }
        catch (CrivonftException e)
        {
            return IntegracoesEndpoints.Crivonft502(e);
        }
    }
}
