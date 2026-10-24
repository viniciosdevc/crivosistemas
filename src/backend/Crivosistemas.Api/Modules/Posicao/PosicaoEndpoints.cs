using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Vendas;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Estoque;
using Crivosistemas.Domain.Financeiro;
using Crivosistemas.Domain.Posicao;
using Crivosistemas.Infrastructure.Cambios;
using Crivosistemas.Infrastructure.Estoque;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Posicao;

public sealed record SaldosSistemaDto(decimal Brl, decimal Usd, decimal Cambios, decimal Esmeraldas, decimal CustoMedioUsd, decimal TotalOperacionalBrl);
public sealed record PosicaoDto(Guid Id, DateOnly Data,
    decimal? BrlInformado, decimal? UsdInformado, decimal? CambiosInformado, decimal? EsmeraldasInformado,
    decimal BrlSistema, decimal UsdSistema, decimal CambiosSistema, decimal EsmeraldasSistema,
    decimal? BrlDiferenca, decimal? UsdDiferenca, decimal? CambiosDiferenca, decimal? EsmeraldasDiferenca,
    decimal CustoMedioUsd, decimal TotalOperacionalBrl, string? Observacao, string? CriadoPorNome, DateTime AtualizadoEm);

public sealed record InformarPosicaoRequest(
    [Range(typeof(decimal), "-100000000", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Valor de caixa inválido.")] decimal? Brl,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O saldo em dólares não pode ser negativo.")] decimal? Usd,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O saldo de câmbios não pode ser negativo.")] decimal? Cambios,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O saldo de esmeraldas não pode ser negativo.")] decimal? Esmeraldas,
    [StringLength(300, ErrorMessage = "Observação muito longa.")] string? Observacao);

/// <summary>Posição diária: informe o que há de verdade em caixa, dólares, câmbios e esmeraldas e veja a diferença para o que o sistema calcula
/// (Financeiro, Estoque e Venda de Câmbios). Não altera nenhum saldo; para corrigir, use "Definir saldo" na tela de origem. Só administradores.</summary>
public static class PosicaoEndpoints
{
    public static void MapPosicaoEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/posicao").WithTags("Posição").RequireAuthorization(Session.AdminPolicy);
        g.MapGet("/sistema", Sistema).WithSummary("Saldos que o sistema calcula agora.");
        g.MapGet("", Listar).WithSummary("Posições registradas por dia (de, ate), mais recentes primeiro.");
        g.MapPut("/hoje", Informar).WithSummary("Registra (ou substitui) a posição de hoje e guarda os saldos do sistema no momento.");
    }

    private static async Task<SaldosSistemaDto> SaldosAsync(AppDbContext db, EstoqueService estoque, CambiosService cambios, CancellationToken ct)
    {
        var somas = await db.LancamentosFinanceiros.AsNoTracking().Where(l => l.Status == StatusLancamento.Confirmado).GroupBy(l => l.Tipo)
            .Select(g => new { Tipo = g.Key, Total = g.Sum(l => l.ValorBrl) }).ToListAsync(ct);
        var brl = (somas.FirstOrDefault(s => s.Tipo == TipoLancamento.Entrada)?.Total ?? 0) - (somas.FirstOrDefault(s => s.Tipo == TipoLancamento.Saida)?.Total ?? 0);
        var usd = await estoque.ResumoAsync(TipoEstoque.Usd, ct);
        var esm = await estoque.ResumoAsync(TipoEstoque.Esmeralda, ct);
        var cmb = await cambios.ObterSaldoAsync(ct);
        return new SaldosSistemaDto(brl, usd.Saldo, cmb, esm.Saldo, usd.CustoMedio, RegrasPosicao.TotalOperacionalBrl(null, brl, null, usd.Saldo, usd.CustoMedio));
    }

    private static PosicaoDto ToDto(PosicaoDiaria p, string? nome) => new(p.Id, p.Data, p.BrlInformado, p.UsdInformado, p.CambiosInformado, p.EsmeraldasInformado,
        p.BrlSistema, p.UsdSistema, p.CambiosSistema, p.EsmeraldasSistema,
        RegrasPosicao.Diferenca(p.BrlInformado, p.BrlSistema), RegrasPosicao.Diferenca(p.UsdInformado, p.UsdSistema),
        RegrasPosicao.Diferenca(p.CambiosInformado, p.CambiosSistema), RegrasPosicao.Diferenca(p.EsmeraldasInformado, p.EsmeraldasSistema),
        p.CustoMedioUsd, RegrasPosicao.TotalOperacionalBrl(p.BrlInformado, p.BrlSistema, p.UsdInformado, p.UsdSistema, p.CustoMedioUsd), p.Observacao, nome, p.AtualizadoEm);

    private static async Task<IResult> Sistema(AppDbContext db, EstoqueService estoque, CambiosService cambios, CancellationToken ct) =>
        Results.Ok(await SaldosAsync(db, estoque, cambios, ct));

    private static async Task<IResult> Listar(AppDbContext db, DateOnly? de, DateOnly? ate, CancellationToken ct)
    {
        var p = VendasEndpoints.Periodo(de, ate, diasPadrao: 30, maxDias: 3700);
        if (p.Erro is not null) return p.Erro;
        var itens = await db.PosicoesDiarias.AsNoTracking().Where(x => x.Data >= p.De && x.Data <= p.Ate).OrderByDescending(x => x.Data).ToListAsync(ct);
        var ids = itens.Where(x => x.CriadoPor != null).Select(x => x.CriadoPor!.Value).Distinct().ToList();
        var nomes = ids.Count == 0 ? [] : await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return Results.Ok(itens.Select(x => ToDto(x, x.CriadoPor is { } u && nomes.TryGetValue(u, out var n) ? n : null)).ToList());
    }

    private static async Task<IResult> Informar([FromBody] InformarPosicaoRequest req, AppDbContext db, EstoqueService estoque, CambiosService cambios, HttpContext http, CancellationToken ct)
    {
        if (req.Brl is null && req.Usd is null && req.Cambios is null && req.Esmeraldas is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["brl"] = ["Informe ao menos um saldo."] });
        var hoje = FusoSp.Hoje();
        var userId = http.User.Id();
        var p = await db.InTransactionAsync(async () =>
        {
            var s = await SaldosAsync(db, estoque, cambios, ct);
            var agora = DateTime.UtcNow;
            var p = await db.PosicoesDiarias.FirstOrDefaultAsync(x => x.Data == hoje, ct);
            if (p is null) db.PosicoesDiarias.Add(p = new PosicaoDiaria { Data = hoje, CriadoPor = userId, CriadoEm = agora });
            p.BrlInformado = req.Brl is { } b ? Math.Round(b, 2, MidpointRounding.AwayFromZero) : null;
            p.UsdInformado = req.Usd is { } u ? Math.Round(u, 8, MidpointRounding.AwayFromZero) : null;
            p.CambiosInformado = req.Cambios is { } c ? Math.Round(c, 2, MidpointRounding.AwayFromZero) : null;
            p.EsmeraldasInformado = req.Esmeraldas is { } e ? Math.Round(e, 8, MidpointRounding.AwayFromZero) : null;
            p.BrlSistema = s.Brl; p.UsdSistema = s.Usd; p.CambiosSistema = s.Cambios; p.EsmeraldasSistema = s.Esmeraldas; p.CustoMedioUsd = s.CustoMedioUsd;
            p.Observacao = string.IsNullOrWhiteSpace(req.Observacao) ? null : req.Observacao.Trim();
            p.AtualizadoEm = agora;
            db.Audit(http, "posicao.informada", "PosicaoDiaria", p.Id, new
            {
                p.Data, p.BrlInformado, p.UsdInformado, p.CambiosInformado, p.EsmeraldasInformado,
                difBrl = RegrasPosicao.Diferenca(p.BrlInformado, p.BrlSistema), difUsd = RegrasPosicao.Diferenca(p.UsdInformado, p.UsdSistema),
                difCambios = RegrasPosicao.Diferenca(p.CambiosInformado, p.CambiosSistema), difEsmeraldas = RegrasPosicao.Diferenca(p.EsmeraldasInformado, p.EsmeraldasSistema),
            });
            await db.SaveChangesAsync(ct);
            return p;
        }, ct);
        var nome = userId is { } id ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Name).FirstOrDefaultAsync(ct) : null;
        return Results.Ok(ToDto(p, nome));
    }
}
