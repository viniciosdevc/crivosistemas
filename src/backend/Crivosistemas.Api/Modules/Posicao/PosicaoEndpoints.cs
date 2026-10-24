using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Vendas;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Posicao;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Persistence;
using Crivosistemas.Infrastructure.Posicao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Posicao;

public sealed record PosicaoDto(Guid Id, DateOnly Data,
    decimal? BrlInformado, decimal? UsdInformado, decimal? CambiosInformado, decimal? EsmeraldasInformado,
    decimal BrlSistema, decimal UsdSistema, decimal CambiosSistema, decimal EsmeraldasSistema,
    decimal? AjusteBrl, decimal? AjusteUsd, decimal? AjusteCambios, decimal? AjusteEsmeraldas,
    decimal BrlFinal, decimal UsdFinal, decimal CambiosFinal, decimal EsmeraldasFinal,
    decimal PrecoUsd, decimal BrlPorCambio, decimal BrlPorEsmeralda, decimal TotalBrl,
    string? Observacao, string? CriadoPorNome, DateTime AtualizadoEm);
public sealed record PosicaoAtualDto(Patrimonio Patrimonio, PosicaoDto? Hoje);

public sealed record InformarPosicaoRequest(
    [Range(typeof(decimal), "-100000000", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Valor da conta inválido.")] decimal? Brl,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O saldo em dólares não pode ser negativo.")] decimal? Usd,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O saldo de câmbios não pode ser negativo.")] decimal? Cambios,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O saldo de esmeraldas não pode ser negativo.")] decimal? Esmeraldas,
    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Cotação inválida.")] decimal? CotacaoUsd,
    [StringLength(300, ErrorMessage = "Observação muito longa.")] string? Observacao);

/// <summary>Posição financeira, como a do crivonft: patrimônio de agora (conta em R$, dólar ao preço médio de venda, câmbios, esmeraldas) e
/// o ajuste diário: você informa os saldos reais e o sistema lança a diferença (Financeiro, Estoque e Venda de Câmbios). Só administradores.</summary>
public static class PosicaoEndpoints
{
    public static void MapPosicaoEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/posicao").WithTags("Posição").RequireAuthorization(Session.AdminPolicy);
        g.MapGet("/atual", Atual).WithSummary("Patrimônio de agora e a posição registrada hoje.");
        g.MapGet("", Listar).WithSummary("Posições registradas por dia (de, ate), mais recentes primeiro.");
        g.MapPut("/hoje", Informar).WithSummary("Informa os saldos reais de hoje e ajusta o sistema pela diferença (campo em branco não é ajustado).");
    }

    private static PosicaoDto ToDto(PosicaoDiaria p, string? nome) => new(p.Id, p.Data, p.BrlInformado, p.UsdInformado, p.CambiosInformado, p.EsmeraldasInformado,
        p.BrlSistema, p.UsdSistema, p.CambiosSistema, p.EsmeraldasSistema, p.AjusteBrl, p.AjusteUsd, p.AjusteCambios, p.AjusteEsmeraldas,
        p.BrlFinal, p.UsdFinal, p.CambiosFinal, p.EsmeraldasFinal, p.PrecoUsd, p.BrlPorCambio, p.BrlPorEsmeralda,
        RegrasPosicao.TotalBrl(p.BrlFinal, p.UsdFinal, p.CambiosFinal, p.EsmeraldasFinal, p.PrecoUsd, p.BrlPorCambio, p.BrlPorEsmeralda),
        p.Observacao, nome, p.AtualizadoEm);

    private static async Task<Dictionary<Guid, string>> NomesAsync(AppDbContext db, IEnumerable<PosicaoDiaria> itens, CancellationToken ct)
    {
        var ids = itens.Where(x => x.CriadoPor != null).Select(x => x.CriadoPor!.Value).Distinct().ToList();
        return ids.Count == 0 ? [] : await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }

    private static string? Nome(IReadOnlyDictionary<Guid, string> nomes, PosicaoDiaria x) => x.CriadoPor is { } u && nomes.TryGetValue(u, out var n) ? n : null;

    private static async Task<IResult> Atual(AppDbContext db, PosicaoService svc, CancellationToken ct)
    {
        var hoje = FusoSp.Hoje();
        var p = await db.PosicoesDiarias.AsNoTracking().FirstOrDefaultAsync(x => x.Data == hoje, ct);
        var nomes = p is null ? [] : await NomesAsync(db, [p], ct);
        return Results.Ok(new PosicaoAtualDto(await svc.AtualAsync(ct), p is null ? null : ToDto(p, Nome(nomes, p))));
    }

    private static async Task<IResult> Listar(AppDbContext db, DateOnly? de, DateOnly? ate, CancellationToken ct)
    {
        var p = VendasEndpoints.Periodo(de, ate, diasPadrao: 60, maxDias: 3700);
        if (p.Erro is not null) return p.Erro;
        var itens = await db.PosicoesDiarias.AsNoTracking().Where(x => x.Data >= p.De && x.Data <= p.Ate).OrderByDescending(x => x.Data).ToListAsync(ct);
        var nomes = await NomesAsync(db, itens, ct);
        return Results.Ok(itens.Select(x => ToDto(x, Nome(nomes, x))).ToList());
    }

    private static async Task<IResult> Informar([FromBody] InformarPosicaoRequest req, AppDbContext db, PosicaoService svc, HttpContext http, CancellationToken ct)
    {
        if (req.Brl is null && req.Usd is null && req.Cambios is null && req.Esmeraldas is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["brl"] = ["Informe ao menos um saldo."] });
        var userId = http.User.Id();
        var salva = await db.InTransactionAsync(async () =>
        {
            var p = await svc.RegistrarAsync(new AjusteSolicitado(req.Brl, req.Usd, req.Cambios, req.Esmeraldas, req.CotacaoUsd, req.Observacao), userId, ct);
            db.Audit(http, "posicao.ajustada", "PosicaoDiaria", p.Id, new
            {
                p.Data, p.BrlInformado, p.UsdInformado, p.CambiosInformado, p.EsmeraldasInformado, p.AjusteBrl, p.AjusteUsd, p.AjusteCambios, p.AjusteEsmeraldas,
            });
            await db.SaveChangesAsync(ct);
            return p;
        }, ct);
        var nomes = await NomesAsync(db, [salva], ct);
        return Results.Ok(ToDto(salva, Nome(nomes, salva)));
    }
}
