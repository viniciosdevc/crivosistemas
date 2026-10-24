using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Crivosistemas.Api.Common;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Estoque;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crivosistemas.Api.Modules.Estoque;

public sealed record ItemEstoqueDto(Guid Id, string? Categoria, string Nome, string? ImagemUrl, int Quantidade, decimal CustoUnitario, decimal CustoTotal, DateOnly DataCompra,
    decimal? PrecoMercadoBrl, decimal? PrecoMercadoUsd, DateTime? PrecoAtualizadoEm, string Status, decimal? ValorVenda, DateOnly? DataVenda,
    string? Observacao, string? MotivoCancelamento);

public sealed record NovoItemRequest(
    [Required(ErrorMessage = "Informe o nome do item."), StringLength(200, ErrorMessage = "O nome pode ter até 200 caracteres.")] string Nome,
    [StringLength(100, ErrorMessage = "Categoria muito longa.")] string? Categoria,
    [Range(1, 100000, ErrorMessage = "A quantidade deve ser de 1 a 100000.")] int Quantidade,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O valor pago (US$) não pode ser negativo.")] decimal CustoUnitario,
    [Required(ErrorMessage = "Informe a data da compra.")] DateOnly? Data,
    [StringLength(300, ErrorMessage = "Observação muito longa.")] string? Observacao);
public sealed record VenderItemRequest(
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O valor da venda não pode ser negativo.")] decimal ValorVenda,
    [Required(ErrorMessage = "Informe a data da venda.")] DateOnly? Data);

/// <summary>Estoque de itens (NFTs) comprados para revenda: o que foi pago, o preço de mercado do catálogo e o lucro potencial.
/// O servidor busca foto e preço no catálogo do crivonft; o que o navegador manda é só o nome e o que foi pago.</summary>
public static class ItensEstoqueEndpoints
{
    public static void MapItensEstoqueEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/estoque/itens").WithTags("Estoque").RequireAuthorization();
        g.MapGet("", Listar).WithSummary("Itens do estoque (?status=estoque|vendido|cancelado; sem status vêm todos), mais recentes primeiro.");
        g.MapPost("", Criar).WithSummary("Cadastra um item pelo nome e pelo valor pago; foto e preço de mercado vêm do catálogo.");
        g.MapPost("/{id:guid}/vender", Vender).WithSummary("Marca o item como vendido com o valor recebido.");
        g.MapPost("/{id:guid}/cancelar", Cancelar).WithSummary("Cancela um item ainda em estoque (fica no histórico).");
        g.MapPost("/atualizar-precos", AtualizarPrecos).WithSummary("Busca de novo o preço de mercado dos itens em estoque.");
    }

    private static string Nome(StatusItem s) => s switch { StatusItem.EmEstoque => "estoque", StatusItem.Vendido => "vendido", _ => "cancelado" };

    private static ItemEstoqueDto ToDto(ItemEstoque i) => new(i.Id, i.Categoria, i.Nome, i.ImagemUrl, i.Quantidade, i.CustoUnitario, i.CustoTotal, i.DataCompra,
        i.PrecoMercadoBrl, i.PrecoMercadoUsd, i.PrecoAtualizadoEm,
        Nome(i.Status), i.ValorVenda, i.DataVenda, i.Observacao, i.MotivoCancelamento);

    private static async Task<IResult> Listar(AppDbContext db, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        StatusItem? s = status switch { "estoque" => StatusItem.EmEstoque, "vendido" => StatusItem.Vendido, "cancelado" => StatusItem.Cancelado, _ => null };
        var r = await db.ItensEstoque.AsNoTracking().Where(i => s == null || i.Status == s)
            .OrderByDescending(i => i.DataCompra).ThenByDescending(i => i.CriadoEm).ToPageAsync(page, pageSize, ct);
        return Results.Ok(new PagedResult<ItemEstoqueDto>(r.Items.Select(ToDto).ToList(), r.Page, r.PageSize, r.Total));
    }

    /// <summary>Acha o item no catálogo pelo nome (e categoria, quando informada). Nulo se o crivonft não estiver configurado, não achar ou falhar.</summary>
    private static async Task<CatalogoItem?> BuscarNoCatalogo(IOptions<CrivonftOptions> o, ICrivonftClient crivonft, string nome, string? categoria, CancellationToken ct)
    {
        if (!o.Value.Envia) return null;
        try
        {
            var r = await crivonft.ChamarGestaoAsync(HttpMethod.Get, $"/api/integracoes/gestao/nfts?q={Uri.EscapeDataString(nome)}&page=1&pageSize=8", null, ct);
            if (!r.Sucesso) return null;
            using var doc = JsonDocument.Parse(r.Corpo);
            if (!doc.RootElement.TryGetProperty("itens", out var itens) || itens.ValueKind != JsonValueKind.Array) return null;
            foreach (var e in itens.EnumerateArray())
            {
                var n = e.TryGetProperty("nome", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() : null;
                var c = e.TryGetProperty("categoria", out var pc) && pc.ValueKind == JsonValueKind.String ? pc.GetString() : null;
                if (!string.Equals(n, nome, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(categoria) && !string.Equals(c, categoria, StringComparison.OrdinalIgnoreCase)) continue;
                var img = e.TryGetProperty("imagemUrl", out var pi) && pi.ValueKind == JsonValueKind.String ? pi.GetString() : null;
                decimal? preco = e.TryGetProperty("precoVendaBrl", out var pp) && pp.ValueKind == JsonValueKind.Number ? pp.GetDecimal() : null;
                decimal? usd = e.TryGetProperty("custoUsd", out var pu) && pu.ValueKind == JsonValueKind.Number ? pu.GetDecimal() : null;
                return new CatalogoItem(n!, c, img, preco, usd);
            }
        }
        catch (Exception e) when (e is CrivonftException or JsonException or HttpRequestException or TaskCanceledException) { /* catálogo fora do ar: o item entra sem foto e sem preço */ }
        return null;
    }

    private sealed record CatalogoItem(string Nome, string? Categoria, string? ImagemUrl, decimal? PrecoBrl, decimal? PrecoUsd);

    private static async Task<IResult> Criar([FromBody] NovoItemRequest req, AppDbContext db, IOptions<CrivonftOptions> o, ICrivonftClient crivonft, HttpContext http, CancellationToken ct)
    {
        var nome = req.Nome.Trim();
        var cat = await BuscarNoCatalogo(o, crivonft, nome, req.Categoria, ct);
        var item = new ItemEstoque
        {
            Nome = cat?.Nome ?? nome,
            Categoria = cat?.Categoria ?? (string.IsNullOrWhiteSpace(req.Categoria) ? null : req.Categoria.Trim()),
            ImagemUrl = cat?.ImagemUrl,
            Quantidade = req.Quantidade,
            CustoUnitario = req.CustoUnitario,
            DataCompra = req.Data!.Value,
            PrecoMercadoBrl = cat?.PrecoBrl,
            PrecoMercadoUsd = cat?.PrecoUsd,
            PrecoAtualizadoEm = cat?.PrecoUsd is null ? null : DateTime.UtcNow,
            Observacao = string.IsNullOrWhiteSpace(req.Observacao) ? null : req.Observacao.Trim(),
            CriadoPor = http.User.Id(),
        };
        db.ItensEstoque.Add(item);
        db.Audit(http, "estoque.item_cadastrado", "ItemEstoque", item.Id, new { item.Nome, item.Quantidade, item.CustoUnitario });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/estoque/itens/{item.Id}", ToDto(item));
    }

    private static async Task<ItemEstoque> Achar(AppDbContext db, Guid id, CancellationToken ct) =>
        await db.ItensEstoque.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Item não encontrado.");

    private static async Task<IResult> Vender(Guid id, [FromBody] VenderItemRequest req, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var item = await db.InTransactionAsync(async () =>
        {
            var i = await Achar(db, id, ct);
            i.Vender(req.ValorVenda, req.Data!.Value);
            db.Audit(http, "estoque.item_vendido", "ItemEstoque", i.Id, new { i.Nome, req.ValorVenda, data = req.Data });
            await db.SaveChangesAsync(ct);
            return i;
        }, ct);
        return Results.Ok(ToDto(item));
    }

    private static async Task<IResult> Cancelar(Guid id, [FromBody] CancelarRequest? req, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var item = await db.InTransactionAsync(async () =>
        {
            var i = await Achar(db, id, ct);
            i.Cancelar(req?.Motivo);
            db.Audit(http, "estoque.item_cancelado", "ItemEstoque", i.Id, new { i.Nome, motivo = req?.Motivo });
            await db.SaveChangesAsync(ct);
            return i;
        }, ct);
        return Results.Ok(ToDto(item));
    }

    private static async Task<IResult> AtualizarPrecos(AppDbContext db, IOptions<CrivonftOptions> o, ICrivonftClient crivonft, CancellationToken ct)
    {
        if (!o.Value.Envia)
            return Results.Json(new { title = "A integração com o crivonft não está configurada.", code = "integracao_nao_configurada", status = 503 }, statusCode: 503);
        var itens = await db.ItensEstoque.Where(i => i.Status == StatusItem.EmEstoque).ToListAsync(ct);
        var atualizados = 0;
        // Um nome só é consultado uma vez, mesmo com vários lotes dele.
        foreach (var grupo in itens.GroupBy(i => (i.Nome.ToLowerInvariant(), i.Categoria?.ToLowerInvariant())))
        {
            var cat = await BuscarNoCatalogo(o, crivonft, grupo.First().Nome, grupo.First().Categoria, ct);
            if (cat?.PrecoUsd is null) continue;
            foreach (var i in grupo)
            {
                i.PrecoMercadoBrl = cat.PrecoBrl;
                i.PrecoMercadoUsd = cat.PrecoUsd;
                i.PrecoAtualizadoEm = DateTime.UtcNow;
                i.ImagemUrl ??= cat.ImagemUrl;
                atualizados++;
            }
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { atualizados, total = itens.Count });
    }
}
