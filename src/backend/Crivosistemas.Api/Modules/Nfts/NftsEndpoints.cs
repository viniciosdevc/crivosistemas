using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Integracoes;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Crivosistemas.Api.Modules.Nfts;

public sealed record AtualizarNftsRequest([Required(ErrorMessage = "Informe a ação.")] string Acao);
public sealed record NovaColecaoRequest(
    [Required(ErrorMessage = "Informe o nome."), StringLength(200, ErrorMessage = "O nome pode ter até 200 caracteres.")] string Nome,
    string? Categoria,
    [RegularExpression("^(0x[0-9a-fA-F]{40})?$", ErrorMessage = "O contrato deve ser um endereço 0x com 40 hexadecimais.")] string? ContratoOverride,
    [Range(1, 500, ErrorMessage = "As posições devem ficar entre 1 e 500.")] int? TotalPosicoes);

/// <summary>Catálogo de NFTs e coleções da loja. Os dados moram no crivonft: aqui é o painel, que repassa as chamadas assinadas e audita o que muda.</summary>
public static class NftsEndpoints
{
    private static readonly string[] AcoesNft = ["catalogo", "indice", "cancelar-catalogo", "cancelar-indice"];
    private static readonly string[] ParametrosNft = ["categoria", "q", "apenasMudaram", "page", "pageSize"];

    public static void MapNftsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/nfts").WithTags("Nfts").RequireAuthorization(Session.AdminPolicy);
        g.MapGet("", ListarNfts).WithSummary("Catálogo de NFTs do crivonft com preço de venda atual, anterior e variação.");
        // Autocomplete da busca do topo: o operador também consulta preço, então basta estar autenticado.
        app.MapGet("/api/nfts/busca", BuscarNfts).WithTags("Nfts").RequireAuthorization()
            .WithSummary("Até 8 NFTs do catálogo pelo nome (?q=), com imagem e custo em USD, para a busca rápida.");
        g.MapPost("/atualizar", AtualizarNfts).WithSummary("Pede ao crivonft para atualizar o catálogo (ou reindexar) em segundo plano.");
        g.MapGet("/colecoes", ListarColecoes).WithSummary("Coleções cadastradas no crivonft.");
        g.MapPost("/colecoes", CriarColecao).WithSummary("Cadastra uma coleção no crivonft.");
    }

    private static async Task<(IResult Resultado, bool Ok)> Chamar(IOptions<CrivonftOptions> o, ICrivonftClient crivonft, HttpMethod metodo, string caminho, object? corpo, CancellationToken ct, int sucesso = StatusCodes.Status200OK)
    {
        if (!o.Value.Envia) return (Results.Json(new { title = "A integração com o crivonft não está configurada.", code = "integracao_nao_configurada", status = 503 }, statusCode: 503), false);
        try
        {
            var r = await crivonft.ChamarGestaoAsync(metodo, caminho, corpo, ct);
            if (r.Sucesso) return (Results.Content(string.IsNullOrWhiteSpace(r.Corpo) ? "{}" : r.Corpo, "application/json", System.Text.Encoding.UTF8, r.StatusHttp == 202 ? 202 : sucesso), true);
            // 409/422 trazem a mensagem do crivonft pensada para quem usa a tela (já existe, inválido, em andamento).
            if (r.StatusHttp is 409 or 422)
            {
                var msg = "O crivonft recusou o pedido.";
                try { if (JsonDocument.Parse(r.Corpo).RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) msg = e.GetString() ?? msg; }
                catch (JsonException) { /* corpo fora do padrão: fica a mensagem genérica */ }
                return (Results.Json(new { title = msg, detail = msg, code = "crivonft_recusou", status = r.StatusHttp }, statusCode: r.StatusHttp), false);
            }
            return (IntegracoesEndpoints.Crivonft502(new CrivonftException($"HTTP {r.StatusHttp}: {(r.Corpo.Length > 200 ? r.Corpo[..200] : r.Corpo)}", r.StatusHttp)), false);
        }
        catch (CrivonftException e)
        {
            return (IntegracoesEndpoints.Crivonft502(e), false);
        }
    }

    private static async Task<IResult> ListarNfts(HttpContext http, IOptions<CrivonftOptions> o, ICrivonftClient crivonft, CancellationToken ct)
    {
        var q = ParametrosNft.Where(k => !string.IsNullOrWhiteSpace(http.Request.Query[k])).Select(k => $"{k}={Uri.EscapeDataString(http.Request.Query[k].ToString())}");
        return (await Chamar(o, crivonft, HttpMethod.Get, "/api/integracoes/gestao/nfts?" + string.Join('&', q), null, ct)).Resultado;
    }

    private static async Task<IResult> BuscarNfts(HttpContext http, IOptions<CrivonftOptions> o, ICrivonftClient crivonft, CancellationToken ct)
    {
        var q = http.Request.Query["q"].ToString().Trim();
        if (q.Length < 2) return Results.Json(new { itens = Array.Empty<object>() });
        return (await Chamar(o, crivonft, HttpMethod.Get, $"/api/integracoes/gestao/nfts?q={Uri.EscapeDataString(q)}&page=1&pageSize=8", null, ct)).Resultado;
    }

    private static async Task<IResult> AtualizarNfts([FromBody] AtualizarNftsRequest req, AppDbContext db, IOptions<CrivonftOptions> o, ICrivonftClient crivonft, HttpContext http, CancellationToken ct)
    {
        if (!AcoesNft.Contains(req.Acao)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["acao"] = ["Ação inválida."] });
        var (r, ok) = await Chamar(o, crivonft, HttpMethod.Post, "/api/integracoes/gestao/nfts/atualizar", new { acao = req.Acao }, ct);
        if (!ok) return r;
        db.Audit(http, "nfts.atualizacao", "Nfts", null, new { req.Acao });
        await db.SaveChangesAsync(ct);
        return r;
    }

    private static async Task<IResult> ListarColecoes(IOptions<CrivonftOptions> o, ICrivonftClient crivonft, CancellationToken ct) =>
        (await Chamar(o, crivonft, HttpMethod.Get, "/api/integracoes/gestao/nfts/colecoes", null, ct)).Resultado;

    private static async Task<IResult> CriarColecao([FromBody] NovaColecaoRequest req, AppDbContext db, IOptions<CrivonftOptions> o, ICrivonftClient crivonft, HttpContext http, CancellationToken ct)
    {
        var corpo = new
        {
            nome = req.Nome.Trim(),
            categoria = string.IsNullOrWhiteSpace(req.Categoria) ? null : req.Categoria.Trim(),
            contratoOverride = string.IsNullOrWhiteSpace(req.ContratoOverride) ? null : req.ContratoOverride.Trim(),
            totalPosicoes = req.TotalPosicoes,
        };
        var (r, ok) = await Chamar(o, crivonft, HttpMethod.Post, "/api/integracoes/gestao/nfts/colecoes", corpo, ct, StatusCodes.Status201Created);
        if (!ok) return r;
        db.Audit(http, "nfts.colecao_cadastrada", "Nfts", null, new { corpo.nome, corpo.categoria });
        await db.SaveChangesAsync(ct);
        return r;
    }
}
