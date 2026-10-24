using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Vendas;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Financeiro;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Financeiro;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Financeiro;

public sealed record LancamentoDto(Guid Id, string Tipo, string Status, string Origem, string Categoria, string? Descricao, decimal ValorBrl, DateOnly Data,
    Guid? VendaId, Guid? LoteEstoqueId, Guid? MovimentoCambiosId, string? MotivoCancelamento, string? CriadoPorNome, DateTime CriadoEm, DateTime? ConfirmadoEm, DateTime? CanceladoEm, bool Automatico);
public sealed record TotaisFinanceiroDto(decimal Entradas, decimal Saidas, decimal Resultado);
public sealed record CategoriaTotalDto(string Categoria, string Tipo, decimal Total, int Quantidade);
public sealed record ResumoFinanceiroDto(decimal Saldo, TotaisFinanceiroDto Hoje, TotaisFinanceiroDto Mes, DateOnly De, DateOnly Ate, TotaisFinanceiroDto Periodo,
    decimal PendenteReceber, decimal PendentePagar, int Pendentes, IReadOnlyList<CategoriaTotalDto> PorCategoria);

public sealed record NovoLancamentoRequest(
    [Required(ErrorMessage = "Informe se é entrada ou saída.")] string Tipo,
    [Required(ErrorMessage = "Informe a categoria."), StringLength(60, MinimumLength = 1, ErrorMessage = "A categoria deve ter até 60 caracteres.")] string Categoria,
    [StringLength(300, ErrorMessage = "Descrição muito longa.")] string? Descricao,
    [Range(typeof(decimal), "0.01", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Informe um valor maior que zero.")] decimal ValorBrl,
    [Required(ErrorMessage = "Informe a data.")] DateOnly? Data,
    bool Pendente = false);
public sealed record CancelarLancamentoRequest([StringLength(300, ErrorMessage = "Motivo muito longo.")] string? Motivo);

/// <summary>Financeiro: entradas e saídas em R$. O PIX das vendas e as compras de dólar entram sozinhos (veja FinanceiroService);
/// o resto é lançado à mão. Lançamento não é editado nem apagado: errou, cancela com motivo e lança de novo. Só administradores.</summary>
public static class FinanceiroEndpoints
{
    public static void MapFinanceiroEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/financeiro").WithTags("Financeiro").RequireAuthorization(Session.AdminPolicy);
        g.MapGet("/resumo", Resumo).WithSummary("Saldo (confirmados), resultado de hoje, do mês e do período, pendências e totais por categoria.");
        g.MapGet("/lancamentos", Listar).WithSummary("Lançamentos com filtros: de, ate, tipo, status, origem, busca.");
        g.MapGet("/categorias", Categorias).WithSummary("Categorias sugeridas e já usadas.");
        g.MapPost("/sincronizar", Sincronizar).WithSummary("Gera os lançamentos de vendas PIX e compras de dólar anteriores ao financeiro (idempotente).");
        g.MapPost("/lancamentos", Criar).WithSummary("Lança uma entrada ou saída manual (confirmada ou pendente).");
        g.MapPost("/lancamentos/{id:guid}/confirmar", Confirmar).WithSummary("Confirma um lançamento manual pendente (o dinheiro entrou ou saiu).");
        g.MapPost("/lancamentos/{id:guid}/cancelar", Cancelar).WithSummary("Cancela (estorna) um lançamento manual, com motivo.");
    }

    private static async Task<TotaisFinanceiroDto> TotaisAsync(IQueryable<LancamentoFinanceiro> q, CancellationToken ct)
    {
        var somas = await q.Where(l => l.Status == StatusLancamento.Confirmado).GroupBy(l => l.Tipo)
            .Select(g => new { Tipo = g.Key, Total = g.Sum(l => l.ValorBrl) }).ToListAsync(ct);
        var e = somas.FirstOrDefault(s => s.Tipo == TipoLancamento.Entrada)?.Total ?? 0;
        var s = somas.FirstOrDefault(s => s.Tipo == TipoLancamento.Saida)?.Total ?? 0;
        return new TotaisFinanceiroDto(e, s, e - s);
    }

    private static async Task<IResult> Resumo(AppDbContext db, DateOnly? de, DateOnly? ate, CancellationToken ct)
    {
        var hoje = FusoSp.Hoje();
        var p = VendasEndpoints.Periodo(de, ate, diasPadrao: 30, maxDias: 3700);
        if (p.Erro is not null) return p.Erro;
        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var l = db.LancamentosFinanceiros.AsNoTracking();

        var saldo = await TotaisAsync(l, ct);
        var totHoje = await TotaisAsync(l.Where(x => x.Data == hoje), ct);
        var totMes = await TotaisAsync(l.Where(x => x.Data >= inicioMes && x.Data <= hoje), ct);
        var periodo = l.Where(x => x.Data >= p.De && x.Data <= p.Ate);
        var totPeriodo = await TotaisAsync(periodo, ct);

        var pend = await l.Where(x => x.Status == StatusLancamento.Pendente).GroupBy(x => x.Tipo)
            .Select(g => new { Tipo = g.Key, Total = g.Sum(x => x.ValorBrl), N = g.Count() }).ToListAsync(ct);
        var porCategoria = await periodo.Where(x => x.Status == StatusLancamento.Confirmado).GroupBy(x => new { x.Categoria, x.Tipo })
            .Select(g => new { g.Key.Categoria, g.Key.Tipo, Total = g.Sum(x => x.ValorBrl), N = g.Count() }).ToListAsync(ct);

        return Results.Ok(new ResumoFinanceiroDto(saldo.Resultado, totHoje, totMes, p.De, p.Ate, totPeriodo,
            pend.FirstOrDefault(x => x.Tipo == TipoLancamento.Entrada)?.Total ?? 0,
            pend.FirstOrDefault(x => x.Tipo == TipoLancamento.Saida)?.Total ?? 0,
            pend.Sum(x => x.N),
            porCategoria.OrderByDescending(c => c.Total).Select(c => new CategoriaTotalDto(c.Categoria, RegrasFinanceiro.Tipo(c.Tipo), c.Total, c.N)).ToList()));
    }

    private static async Task<IResult> Listar(AppDbContext db, DateOnly? de, DateOnly? ate, string? tipo, string? status, string? origem, string? busca,
        int? page, int? pageSize, CancellationToken ct)
    {
        var q = db.LancamentosFinanceiros.AsNoTracking().AsQueryable();
        var erros = new Dictionary<string, string[]>();
        if (de is not null) q = q.Where(l => l.Data >= de);
        if (ate is not null) q = q.Where(l => l.Data <= ate);
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            if (!RegrasFinanceiro.TentarTipo(tipo, out var t)) erros["tipo"] = ["Use entrada ou saida."]; else q = q.Where(l => l.Tipo == t);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!RegrasFinanceiro.TentarStatus(status, out var s)) erros["status"] = ["Use pendente, confirmado ou cancelado."]; else q = q.Where(l => l.Status == s);
        }
        if (!string.IsNullOrWhiteSpace(origem))
        {
            OrigemLancamento? o = origem.Trim().ToLowerInvariant() switch
            {
                "manual" => OrigemLancamento.Manual, "venda" => OrigemLancamento.Venda, "compra_usd" => OrigemLancamento.CompraUsd, "venda_cambios" => OrigemLancamento.VendaCambios, _ => null,
            };
            if (o is null) erros["origem"] = ["Use manual, venda, venda_cambios ou compra_usd."]; else q = q.Where(l => l.Origem == o);
        }
        if (erros.Count > 0) return Results.ValidationProblem(erros);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim();
            q = q.Where(l => l.Categoria.Contains(b) || (l.Descricao != null && l.Descricao.Contains(b)));
        }

        var r = await q.OrderByDescending(l => l.Data).ThenByDescending(l => l.CriadoEm).ThenByDescending(l => l.Id).ToPageAsync(page, pageSize, ct);
        var nomes = await NomesAsync(db, r.Items, ct);
        return Results.Ok(new PagedResult<LancamentoDto>(r.Items.Select(x => ToDto(x, nomes)).ToList(), r.Page, r.PageSize, r.Total));
    }

    private static async Task<Dictionary<Guid, string>> NomesAsync(AppDbContext db, IEnumerable<LancamentoFinanceiro> itens, CancellationToken ct)
    {
        var ids = itens.Where(x => x.CriadoPor != null).Select(x => x.CriadoPor!.Value).Distinct().ToList();
        return ids.Count == 0 ? [] : await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }

    private static LancamentoDto ToDto(LancamentoFinanceiro l, IReadOnlyDictionary<Guid, string> nomes) => new(l.Id, RegrasFinanceiro.Tipo(l.Tipo),
        RegrasFinanceiro.Status(l.Status), RegrasFinanceiro.Origem(l.Origem), l.Categoria, l.Descricao, l.ValorBrl, l.Data, l.VendaId, l.LoteEstoqueId, l.MovimentoCambiosId,
        l.MotivoCancelamento, l.CriadoPor is { } u && nomes.TryGetValue(u, out var n) ? n : null, l.CriadoEm, l.ConfirmadoEm, l.CanceladoEm, l.Automatico);

    private static async Task<IResult> Categorias(AppDbContext db, CancellationToken ct)
    {
        var usadas = await db.LancamentosFinanceiros.AsNoTracking().Select(l => l.Categoria).Distinct().ToListAsync(ct);
        return Results.Ok(RegrasFinanceiro.CategoriasSugeridas.Concat(usadas.Order(StringComparer.CurrentCultureIgnoreCase))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    private static async Task<IResult> Sincronizar(FinanceiroService svc, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var (vendas, compras) = await db.InTransactionAsync(async () =>
        {
            var r = await svc.SincronizarTudoAsync(ct);
            db.Audit(http, "financeiro.sincronizado", "LancamentoFinanceiro", null, new { r.Vendas, r.Compras });
            await db.SaveChangesAsync(ct);
            return r;
        }, ct);
        return Results.Ok(new { vendas, compras });
    }

    private static async Task<IResult> Criar([FromBody] NovoLancamentoRequest req, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!RegrasFinanceiro.TentarTipo(req.Tipo, out var tipo))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["tipo"] = ["Use entrada ou saida."] });
        var categoria = req.Categoria.Trim();
        if (categoria.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["categoria"] = ["Informe a categoria."] });

        var agora = DateTime.UtcNow;
        var l = new LancamentoFinanceiro
        {
            Tipo = tipo, Origem = OrigemLancamento.Manual, Categoria = categoria,
            Descricao = string.IsNullOrWhiteSpace(req.Descricao) ? null : req.Descricao.Trim(),
            ValorBrl = Math.Round(req.ValorBrl, 2, MidpointRounding.AwayFromZero), Data = req.Data!.Value,
            Status = req.Pendente ? StatusLancamento.Pendente : StatusLancamento.Confirmado,
            ConfirmadoEm = req.Pendente ? null : agora, CriadoPor = http.User.Id(), CriadoEm = agora, AtualizadoEm = agora,
        };
        await db.InTransactionAsync(async () =>
        {
            db.LancamentosFinanceiros.Add(l);
            db.Audit(http, "financeiro.lancamento_criado", "LancamentoFinanceiro", l.Id,
                new { tipo = RegrasFinanceiro.Tipo(tipo), l.Categoria, l.ValorBrl, l.Data, req.Pendente });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
        return Results.Created($"/api/financeiro/lancamentos/{l.Id}", ToDto(l, await NomesAsync(db, [l], ct)));
    }

    private static async Task<IResult> Alterar(Guid id, AppDbContext db, HttpContext http, string acao, Action<LancamentoFinanceiro> mudar, object? dados, CancellationToken ct)
    {
        var l = await db.InTransactionAsync(async () =>
        {
            var l = await db.LancamentosFinanceiros.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lançamento não encontrado.");
            mudar(l);
            db.Audit(http, acao, "LancamentoFinanceiro", l.Id, dados);
            await db.SaveChangesAsync(ct);
            return l;
        }, ct);
        return Results.Ok(ToDto(l, await NomesAsync(db, [l], ct)));
    }

    private static Task<IResult> Confirmar(Guid id, AppDbContext db, HttpContext http, CancellationToken ct) =>
        Alterar(id, db, http, "financeiro.lancamento_confirmado", l => l.Confirmar(DateTime.UtcNow), null, ct);

    private static Task<IResult> Cancelar(Guid id, [FromBody] CancelarLancamentoRequest? req, AppDbContext db, HttpContext http, CancellationToken ct) =>
        Alterar(id, db, http, "financeiro.lancamento_cancelado", l => l.Cancelar(req?.Motivo, DateTime.UtcNow), new { motivo = req?.Motivo }, ct);
}
