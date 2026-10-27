using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Domain.Metas;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Metas;

public sealed record MetaSemanaRequest([Required(ErrorMessage = "Informe a semana.")] DateOnly? SemanaInicio, decimal ValorBrl);
public sealed record SalvarMetasRequest([Required(ErrorMessage = "Informe as metas."), MinLength(1, ErrorMessage = "Informe ao menos uma meta.")] List<MetaSemanaRequest> Metas);

public sealed record DiaMetaDto(DateOnly Dia, decimal LucroBrl, decimal ReceitaBrl, int Vendas);
public sealed record SemanaMetaDto(DateOnly SemanaInicio, decimal MetaBrl, bool Definida, decimal LucroBrl, decimal ReceitaBrl, int Vendas, bool Bateu, bool Atual);
public sealed record PainelMetasDto(DateOnly Hoje, DateOnly SemanaInicio, DateOnly SemanaFim, decimal PadraoBrl, ResumoMeta Resumo, decimal? LucroSemanaAnteriorBrl,
    int Sequencia, IReadOnlyList<DiaMetaDto> Dias, IReadOnlyList<SemanaMetaDto> Historico, IReadOnlyList<SemanaMetaDto> Planejamento);

/// <summary>Meta semanal de lucro: cadastro por semana (segunda a domingo), acompanhamento contra as vendas faturáveis do Gestão, histórico e mapa de calor.
/// Todos veem o painel; só administradores cadastram metas.</summary>
public static class MetasEndpoints
{
    private const int SemanasMapa = 16;
    private const int SemanasHistorico = 12;
    private const int SemanasPlanejamento = 12;

    public static void MapMetasEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/metas-semanais").WithTags("Metas").RequireAuthorization();
        g.MapGet("", Painel).WithSummary("Painel da semana atual, histórico, mapa de calor e planejamento das próximas semanas.");
        g.MapPut("", Salvar).RequireAuthorization(Session.AdminPolicy).WithSummary("Define a meta de uma ou mais semanas (segundas-feiras) de uma vez.");
    }

    private static async Task<IResult> Painel(AppDbContext db, CancellationToken ct) => Results.Ok(await Montar(db, ct));

    private static async Task<PainelMetasDto> Montar(AppDbContext db, CancellationToken ct)
    {
        var hoje = FusoSp.Hoje();
        var semana = RegrasMetas.Segunda(hoje);
        var mapaInicio = semana.AddDays(-7 * (SemanasMapa - 1));
        var planejamentoFim = semana.AddDays(7 * (SemanasPlanejamento - 1));

        var (ini, fim) = FusoSp.Intervalo(mapaInicio, semana.AddDays(6));
        var linhas = await db.Vendas.AsNoTracking().Where(VendaConsultas.Faturavel).Where(v => v.DataHora >= ini && v.DataHora < fim)
            .Select(v => new { v.DataHora, v.TotalBrl, v.LucroBrl }).ToListAsync(ct);
        var porDia = linhas.GroupBy(l => FusoSp.Dia(l.DataHora))
            .ToDictionary(x => x.Key, x => new DiaMetaDto(x.Key, x.Sum(l => l.LucroBrl), x.Sum(l => l.TotalBrl), x.Count()));
        var salvas = await db.MetasSemanais.AsNoTracking().Where(m => m.SemanaInicio >= mapaInicio && m.SemanaInicio <= planejamentoFim)
            .ToDictionaryAsync(m => m.SemanaInicio, m => m.MetaLucroBrl, ct);

        DiaMetaDto Dia(DateOnly d) => porDia.TryGetValue(d, out var x) ? x : new DiaMetaDto(d, 0, 0, 0);
        SemanaMetaDto Semana(DateOnly inicio)
        {
            var dias = Enumerable.Range(0, 7).Select(i => Dia(inicio.AddDays(i))).ToList();
            var definida = salvas.TryGetValue(inicio, out var meta);
            if (!definida) meta = RegrasMetas.PadraoBrl;
            var lucro = dias.Sum(d => d.LucroBrl);
            return new SemanaMetaDto(inicio, meta, definida, lucro, dias.Sum(d => d.ReceitaBrl), dias.Sum(d => d.Vendas), lucro >= meta, inicio == semana);
        }

        var recentes = Enumerable.Range(0, SemanasHistorico).Select(i => Semana(semana.AddDays(-7 * i))).ToList(); // da atual para trás
        var sequencia = 0;
        foreach (var s in recentes.Where(s => !s.Atual))
        {
            if (!s.Bateu) break;
            sequencia++;
        }
        var atual = recentes[0];
        var anterior = recentes.Count > 1 ? recentes[1] : null;
        var historico = recentes.Where(s => s.Atual || s.Vendas > 0 || s.Definida).ToList();
        var planejamento = Enumerable.Range(0, SemanasPlanejamento).Select(i => Semana(semana.AddDays(7 * i))).ToList();
        var dias112 = Enumerable.Range(0, 7 * SemanasMapa).Select(i => Dia(mapaInicio.AddDays(i))).ToList();

        return new PainelMetasDto(hoje, semana, semana.AddDays(6), RegrasMetas.PadraoBrl, RegrasMetas.Resumir(atual.MetaBrl, atual.LucroBrl, RegrasMetas.DiaDaSemana(hoje)),
            anterior is { LucroBrl: > 0 } ? anterior.LucroBrl : null, sequencia, dias112, historico, planejamento);
    }

    private static async Task<IResult> Salvar([FromBody] SalvarMetasRequest req, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var erros = new Dictionary<string, string[]>();
        if (req.Metas.Count == 0) erros["metas"] = ["Informe ao menos uma meta."];
        if (req.Metas.Count > RegrasMetas.MaxSemanasPorSalvar) erros["metas"] = [$"Salve no máximo {RegrasMetas.MaxSemanasPorSalvar} semanas por vez."];
        var pedidas = new Dictionary<DateOnly, decimal>();
        foreach (var m in req.Metas)
        {
            if (m.SemanaInicio is null) { erros["metas"] = ["Informe a semana de cada meta."]; break; }
            var valor = Math.Round(m.ValorBrl, 2, MidpointRounding.AwayFromZero);
            if (RegrasMetas.Validar(m.SemanaInicio.Value, valor) is { } erro) { erros["metas"] = [erro]; break; }
            pedidas[m.SemanaInicio.Value] = valor;
        }
        if (erros.Count > 0) return Results.ValidationProblem(erros);

        var datas = pedidas.Keys.ToList();
        var existentes = await db.MetasSemanais.Where(x => datas.Contains(x.SemanaInicio)).ToDictionaryAsync(x => x.SemanaInicio, ct);
        var userId = http.User.Id();
        foreach (var (inicio, valor) in pedidas)
        {
            if (existentes.TryGetValue(inicio, out var meta))
            {
                meta.MetaLucroBrl = valor;
                meta.AtualizadoPor = userId;
                meta.AtualizadoEm = DateTime.UtcNow;
            }
            else db.MetasSemanais.Add(new MetaSemanal { SemanaInicio = inicio, MetaLucroBrl = valor, AtualizadoPor = userId });
        }
        db.Audit(http, "meta.semanal.salva", "MetaSemanal", null, new { Metas = pedidas.OrderBy(p => p.Key).Select(p => new { Semana = p.Key, Valor = p.Value }) });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Montar(db, ct));
    }
}
