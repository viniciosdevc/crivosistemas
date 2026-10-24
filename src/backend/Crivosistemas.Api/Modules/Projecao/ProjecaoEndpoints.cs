using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Projecao;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Persistence;
using Crivosistemas.Infrastructure.Posicao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Projecao;

public sealed record CenarioRequest(
    [Required(ErrorMessage = "Dê um nome ao cenário."), StringLength(80, MinimumLength = 1, ErrorMessage = "O nome deve ter até 80 caracteres.")] string Nome,
    [Required(ErrorMessage = "Informe o início do período base.")] DateOnly? BaseDe,
    [Required(ErrorMessage = "Informe o fim do período base.")] DateOnly? BaseAte,
    [Required(ErrorMessage = "Informe até quando projetar.")] DateOnly? DataFinal,
    [Range(typeof(decimal), "-100", "1000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O crescimento deve ficar entre -100% e 1000%.")] decimal CrescimentoPercent = 0);

public sealed record CenarioDto(Guid? Id, string Nome, DateOnly BaseDe, DateOnly BaseAte, DateOnly DataFinal, DateOnly InicioProjecao, decimal CrescimentoPercent,
    decimal MediaFaturamentoDia, decimal MediaLucroDia, decimal CaixaInicialBrl, DateTime? CriadoEm);
public sealed record DiaProjecaoDto(DateOnly Dia, decimal FaturamentoProjetado, decimal LucroProjetado, decimal CaixaProjetado, decimal? FaturamentoRealizado, decimal? LucroRealizado);
/// <summary>Resumo do cenário: o que era esperado até hoje contra o que aconteceu, e onde termina.</summary>
public sealed record ResumoProjecaoDto(decimal LucroFinal, decimal FaturamentoFinal, decimal CaixaFinal, decimal LucroEsperadoAteHoje, decimal LucroRealizadoAteHoje,
    decimal DiferencaLucro, int DiasDecorridos, int DiasTotal);
public sealed record ProjecaoDto(CenarioDto Cenario, ResumoProjecaoDto Resumo, IReadOnlyList<DiaProjecaoDto> Dias);

/// <summary>Projeção: a partir da média diária de vendas e lucro de um período base, projeta o lucro acumulado e o caixa até uma data.
/// Cenários salvos guardam a média da hora em que foram criados e são comparados com o que realmente vendeu. Só administradores (usa o caixa).</summary>
public static class ProjecaoEndpoints
{
    public static void MapProjecaoEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/projecoes").WithTags("Projeção").RequireAuthorization(Session.AdminPolicy);
        g.MapGet("", Listar).WithSummary("Cenários salvos, com o resumo de cada um (projetado × realizado).");
        g.MapPost("/simular", Simular).WithSummary("Calcula a projeção sem salvar (prévia do modal).");
        g.MapGet("/{id:guid}", Detalhe).WithSummary("Cenário com o gráfico dia a dia: projetado e realizado.");
        g.MapPost("", Criar).WithSummary("Salva um cenário (fixa a média do período base e o caixa de agora).");
        g.MapPut("/{id:guid}", Editar).WithSummary("Edita o cenário e recalcula a média pelo período base informado.");
        g.MapDelete("/{id:guid}", Excluir).WithSummary("Apaga um cenário (projeções não são registros financeiros).");
    }

    private static CenarioDto ToDto(CenarioProjecao c) => new(c.Id, c.Nome, c.BaseDe, c.BaseAte, c.DataFinal, c.InicioProjecao, c.CrescimentoPercent,
        c.MediaFaturamentoDia, c.MediaLucroDia, c.CaixaInicialBrl, c.CriadoEm);

    private static IResult? Validar(CenarioRequest r)
    {
        var erros = RegrasProjecao.Validar(r.BaseDe!.Value, r.BaseAte!.Value, r.DataFinal!.Value, FusoSp.Hoje());
        return erros.Count == 0 ? null : Results.ValidationProblem(erros.ToDictionary(e => e.Key, e => new[] { e.Value }));
    }

    /// <summary>Soma diária de vendas faturáveis nos dias [de, ate] (dia de São Paulo).</summary>
    internal static async Task<Dictionary<DateOnly, (decimal Fat, decimal Luc)>> VendasPorDiaAsync(AppDbContext db, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var (ini, fim) = FusoSp.Intervalo(de, ate);
        var linhas = await db.Vendas.AsNoTracking().Where(VendaConsultas.Faturavel).Where(v => v.DataHora >= ini && v.DataHora < fim)
            .Select(v => new { v.DataHora, v.TotalBrl, v.LucroBrl }).ToListAsync(ct);
        return linhas.GroupBy(l => FusoSp.Dia(l.DataHora)).ToDictionary(g => g.Key, g => (g.Sum(x => x.TotalBrl), g.Sum(x => x.LucroBrl)));
    }

    private static async Task<CenarioProjecao> Montar(CenarioRequest r, AppDbContext db, PosicaoService posicao, CancellationToken ct)
    {
        var porDia = await VendasPorDiaAsync(db, r.BaseDe!.Value, r.BaseAte!.Value, ct);
        var caixa = (await posicao.AtualAsync(ct)).CaixaLiquido;
        return new CenarioProjecao
        {
            Nome = r.Nome.Trim(), BaseDe = r.BaseDe.Value, BaseAte = r.BaseAte.Value, DataFinal = r.DataFinal!.Value, CrescimentoPercent = r.CrescimentoPercent,
            MediaFaturamentoDia = RegrasProjecao.MediaDiaria(porDia.Values.Sum(x => x.Fat), r.BaseDe.Value, r.BaseAte.Value),
            MediaLucroDia = RegrasProjecao.MediaDiaria(porDia.Values.Sum(x => x.Luc), r.BaseDe.Value, r.BaseAte.Value),
            CaixaInicialBrl = Math.Round(caixa, 2, MidpointRounding.AwayFromZero),
        };
    }

    private static async Task<ProjecaoDto> Calcular(CenarioProjecao c, AppDbContext db, CancellationToken ct)
    {
        var hoje = FusoSp.Hoje();
        var proj = RegrasProjecao.Projetar(c.MediaFaturamentoDia, c.MediaLucroDia, c.CrescimentoPercent, c.InicioProjecao, c.DataFinal);
        var ultimoReal = hoje < c.DataFinal ? hoje : c.DataFinal;
        var reais = ultimoReal >= c.InicioProjecao ? await VendasPorDiaAsync(db, c.InicioProjecao, ultimoReal, ct) : [];

        decimal fatAc = 0, lucAc = 0;
        var dias = new List<DiaProjecaoDto>(proj.Count);
        foreach (var p in proj)
        {
            decimal? fr = null, lr = null;
            if (p.Dia <= ultimoReal)
            {
                reais.TryGetValue(p.Dia, out var d);
                fatAc += d.Fat;
                lucAc += d.Luc;
                fr = fatAc;
                lr = lucAc;
            }
            dias.Add(new DiaProjecaoDto(p.Dia, p.FaturamentoAcumulado, p.LucroAcumulado, c.CaixaInicialBrl + p.LucroAcumulado, fr, lr));
        }
        var decorridos = dias.Count(d => d.LucroRealizado != null);
        var ultimo = dias.Count > 0 ? dias[^1] : null;
        var esperado = decorridos > 0 ? dias[decorridos - 1].LucroProjetado : 0;
        var realizado = decorridos > 0 ? dias[decorridos - 1].LucroRealizado ?? 0 : 0;
        return new ProjecaoDto(ToDto(c),
            new ResumoProjecaoDto(ultimo?.LucroProjetado ?? 0, ultimo?.FaturamentoProjetado ?? 0, ultimo?.CaixaProjetado ?? c.CaixaInicialBrl, esperado, realizado,
                realizado - esperado, decorridos, dias.Count), dias);
    }

    private static async Task<IResult> Simular([FromBody] CenarioRequest req, AppDbContext db, PosicaoService posicao, CancellationToken ct)
    {
        if (Validar(req) is { } erro) return erro;
        return Results.Ok(await Calcular(await Montar(req, db, posicao, ct), db, ct));
    }

    private static async Task<IResult> Listar(AppDbContext db, CancellationToken ct)
    {
        var cenarios = await db.CenariosProjecao.AsNoTracking().OrderByDescending(c => c.CriadoEm).ToListAsync(ct);
        var lista = new List<ProjecaoDto>();
        foreach (var c in cenarios) lista.Add(await Calcular(c, db, ct));
        return Results.Ok(lista.Select(p => new { p.Cenario, p.Resumo }).ToList());
    }

    private static async Task<IResult> Detalhe(Guid id, AppDbContext db, CancellationToken ct)
    {
        var c = await db.CenariosProjecao.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Cenário não encontrado.");
        return Results.Ok(await Calcular(c, db, ct));
    }

    private static async Task<IResult> Criar([FromBody] CenarioRequest req, AppDbContext db, PosicaoService posicao, HttpContext http, CancellationToken ct)
    {
        if (Validar(req) is { } erro) return erro;
        var c = await Montar(req, db, posicao, ct);
        c.CriadoPor = http.User.Id();
        db.CenariosProjecao.Add(c);
        db.Audit(http, "projecao.criada", "CenarioProjecao", c.Id, new { c.Nome, c.BaseDe, c.BaseAte, c.DataFinal, c.CrescimentoPercent });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Calcular(c, db, ct));
    }

    private static async Task<IResult> Editar(Guid id, [FromBody] CenarioRequest req, AppDbContext db, PosicaoService posicao, HttpContext http, CancellationToken ct)
    {
        if (Validar(req) is { } erro) return erro;
        var c = await db.CenariosProjecao.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Cenário não encontrado.");
        var novo = await Montar(req, db, posicao, ct);
        c.Nome = novo.Nome;
        c.BaseDe = novo.BaseDe;
        c.BaseAte = novo.BaseAte;
        c.DataFinal = novo.DataFinal;
        c.CrescimentoPercent = novo.CrescimentoPercent;
        c.MediaFaturamentoDia = novo.MediaFaturamentoDia;
        c.MediaLucroDia = novo.MediaLucroDia;
        c.CaixaInicialBrl = novo.CaixaInicialBrl;
        c.AtualizadoEm = DateTime.UtcNow;
        db.Audit(http, "projecao.editada", "CenarioProjecao", c.Id, new { c.Nome, c.BaseDe, c.BaseAte, c.DataFinal, c.CrescimentoPercent });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Calcular(c, db, ct));
    }

    private static async Task<IResult> Excluir(Guid id, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var c = await db.CenariosProjecao.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Cenário não encontrado.");
        db.CenariosProjecao.Remove(c);
        db.Audit(http, "projecao.excluida", "CenarioProjecao", c.Id, new { c.Nome });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
