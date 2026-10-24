using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Integracoes;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Persistence;
using Crivosistemas.Infrastructure.Integracoes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Crivosistemas.Api.Modules.Vendas;

public sealed record ClienteResumoDto(Guid Id, string Nickname, string? Email, string? Carteira);
public sealed record VendaListaDto(Guid Id, string Origem, string OrigemId, string? PedidoId, DateTime DataHora, DateOnly Dia, string Status, string StatusNormalizado,
    string? FormaPagamento, string? Vendedor, ClienteResumoDto? Cliente, decimal TotalBrl, decimal LucroBrl, decimal MargemPercent, bool Removida, bool Faturavel, string? Item, decimal ValorPixBrl, decimal ValorCambios);
/// <summary>Soma de todas as vendas faturáveis (pagas/concluídas, não removidas) do filtro, em todas as páginas.</summary>
public sealed record SubtotalVendasDto(int Quantidade, decimal TotalBrl, decimal LucroBrl);
public sealed record VendasPaginaDto(IReadOnlyList<VendaListaDto> Items, int Page, int PageSize, int Total, SubtotalVendasDto Subtotal);
public sealed record VendaItemDto(int Ordem, string Descricao, decimal Quantidade, decimal PrecoUsd, decimal PrecoBrl, string? Referencia);
public sealed record CupomDto(string Codigo, string? Tipo);
public sealed record ValoresDto(decimal TotalBrl, decimal DescontoBrl, decimal TaxaBrl, decimal ValorPixBrl, decimal ValorCambios, decimal CustoBrl, decimal CustoUsd,
    decimal CustoCambios, decimal LucroBrl, decimal MargemPercent);
public sealed record VendaEventoDto(Guid Id, Guid EventoId, string Tipo, string Estado, DateTime OcorridoEm, DateTime RecebidoEm, string? Resultado, string? Erro);
public sealed record VendaManualDto(string? OrigemCusto, decimal? QuantidadeEsmeraldas, bool UsaTaxaPrata, decimal? DolarCompraUsado, string? Observacao,
    IReadOnlyList<MovimentoVendaDto> Movimentos);
public sealed record MovimentoVendaDto(string Tipo, decimal Quantidade, decimal CustoUnitario, string Motivo, DateTime CriadoEm);
public sealed record VendaDetalheDto(VendaListaDto Venda, ValoresDto Valores, CupomDto? Cupom, IReadOnlyList<VendaItemDto> Itens, DateTime AtualizadoEmOrigem, DateTime? RemovidaEm,
    IReadOnlyList<VendaEventoDto> Eventos, VendaManualDto? Manual);

public sealed record TotaisDto(decimal Faturamento, decimal Lucro, decimal TicketMedio, int Quantidade);
public sealed record ResumoOrigemDto(string Origem, decimal Faturamento, decimal Lucro, int Quantidade);
public sealed record ResumoDiaDto(DateOnly Dia, decimal Faturamento, decimal Lucro, int Quantidade);
public sealed record ResumoVendasDto(DateOnly De, DateOnly Ate, TotaisDto Totais, IReadOnlyList<ResumoOrigemDto> PorOrigem, IReadOnlyList<ResumoDiaDto> PorDia);

/// <summary>Categoria de log das vendas.</summary>
public sealed class VendasLog;

/// <summary>Vendas consolidadas (nft, câmbios e site). Datas e dias seguem America/Sao_Paulo; removidas e canceladas ficam fora do faturamento.
/// Qualquer pessoa autenticada (admin ou operador) lê.</summary>
public static class VendasEndpoints
{
    public static void MapVendasEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/vendas").WithTags("Vendas").RequireAuthorization();
        g.MapGet("", Listar).WithSummary("Lista paginada com filtros: de, ate, origem, statusNormalizado, formaPagamento, clienteId, busca, incluirRemovidas.");
        g.MapGet("/resumo", Resumo).WithSummary("Resumo para o Painel: faturamento, lucro, ticket médio, quantidade, por origem e por dia.");
        g.MapGet("/imagens", Imagens).WithSummary("Imagem dos itens pelo nome (?nome=A&nome=B, até 50), vinda do crivonft e guardada em cache. Sem imagem ou sem integração: null.");
        g.MapGet("/{id:guid}", Detalhe).WithSummary("Detalhe: itens, valores, cupom e histórico de eventos.");
    }

    internal static VendaListaDto ToLista(Venda v) => new(v.Id, Nomes.Origem(v.Origem), v.OrigemId, v.PedidoId, v.DataHora, FusoSp.Dia(v.DataHora), v.Status,
        Nomes.Status(v.StatusNormalizado), v.FormaPagamento is { } f ? Nomes.Forma(f) : null, v.Vendedor,
        v.Cliente is null ? null : new ClienteResumoDto(v.Cliente.Id, v.Cliente.Nickname, v.Cliente.Email, v.Cliente.Carteira),
        v.TotalBrl, v.LucroBrl, v.MargemPercent, v.Removida, v.Faturavel, NomeItem(v), v.ValorPixBrl, v.ValorCambios);

    /// <summary>Nome para identificar a venda: o primeiro item e "+N" quando houver mais. Null se os itens não foram carregados.</summary>
    internal static string? NomeItem(Venda v)
    {
        if (v.Itens.Count == 0) return null;
        var primeiro = v.Itens.OrderBy(i => i.Ordem).First().Descricao;
        return v.Itens.Count == 1 ? primeiro : $"{primeiro} +{v.Itens.Count - 1}";
    }

    /// <summary>Período padrão: últimos 30 dias até hoje (SP). Devolve null e o erro quando inválido.</summary>
    internal static (DateOnly De, DateOnly Ate, IResult? Erro) Periodo(DateOnly? de, DateOnly? ate, int diasPadrao = 30, int maxDias = 800)
    {
        var fim = ate ?? FusoSp.Hoje();
        var ini = de ?? fim.AddDays(-(diasPadrao - 1));
        if (ini > fim) return (ini, fim, Results.ValidationProblem(new Dictionary<string, string[]> { ["de"] = ["A data inicial deve ser anterior ou igual à final."] }));
        if (fim.DayNumber - ini.DayNumber > maxDias) return (ini, fim, Results.ValidationProblem(new Dictionary<string, string[]> { ["ate"] = [$"O período máximo é de {maxDias} dias."] }));
        return (ini, fim, null);
    }

    private static async Task<IResult> Listar(AppDbContext db, DateOnly? de, DateOnly? ate, string? origem, string? statusNormalizado, string? status, string? formaPagamento,
        Guid? clienteId, string? busca, bool? incluirRemovidas, string? ordem, string? dir, int? page, int? pageSize, CancellationToken ct)
    {
        var q = db.Vendas.AsNoTracking().AsQueryable();
        var erros = new Dictionary<string, string[]>();

        if (de is not null || ate is not null)
        {
            var p = Periodo(de, ate);
            if (p.Erro is not null) return p.Erro;
            var (ini, fim) = FusoSp.Intervalo(p.De, p.Ate);
            q = q.Where(v => v.DataHora >= ini && v.DataHora < fim);
        }
        if (!string.IsNullOrWhiteSpace(origem))
        {
            if (!Nomes.TentarOrigem(origem, out var o)) erros["origem"] = ["Use nft, cambios, site ou manual."]; else q = q.Where(v => v.Origem == o);
        }
        var st = statusNormalizado ?? status;
        if (!string.IsNullOrWhiteSpace(st))
        {
            if (!Nomes.TentarStatus(st, out var s)) erros["statusNormalizado"] = ["Use pendente, pago, concluido, cancelado ou reembolsado."]; else q = q.Where(v => v.StatusNormalizado == s);
        }
        if (!string.IsNullOrWhiteSpace(formaPagamento))
        {
            if (!Nomes.TentarForma(formaPagamento, out var f)) erros["formaPagamento"] = ["Use pix, cambios ou hibrido."]; else q = q.Where(v => v.FormaPagamento == f);
        }
        if (erros.Count > 0) return Results.ValidationProblem(erros);
        if (clienteId is { } cid) q = q.Where(v => v.ClienteId == cid);
        if (incluirRemovidas != true) q = q.Where(v => v.RemovidaEm == null);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim();
            q = q.Where(v => v.OrigemId.Contains(b) || (v.PedidoId != null && v.PedidoId.Contains(b)) || (v.Vendedor != null && v.Vendedor.Contains(b))
                || (v.CupomCodigo != null && v.CupomCodigo.Contains(b)) || (v.Cliente != null && (v.Cliente.Nickname.Contains(b) || (v.Cliente.Carteira != null && v.Cliente.Carteira.Contains(b)) || (v.Cliente.Email != null && v.Cliente.Email.Contains(b))))
                || v.Itens.Any(i => i.Descricao.Contains(b)));
        }

        var desc = Paging.Descending(dir);
        var ordenado = (ordem?.ToLowerInvariant()) switch
        {
            "total" => desc ? q.OrderByDescending(v => v.TotalBrl) : q.OrderBy(v => v.TotalBrl),
            "lucro" => desc ? q.OrderByDescending(v => v.LucroBrl) : q.OrderBy(v => v.LucroBrl),
            _ => desc ? q.OrderByDescending(v => v.DataHora) : q.OrderBy(v => v.DataHora),
        };
        var r = await ordenado.ThenBy(v => v.Id).Include(v => v.Cliente).Include(v => v.Itens).AsSplitQuery().ToPageAsync(page, pageSize, ct);
        var fat = await q.Where(VendaConsultas.Faturavel).GroupBy(_ => 1)
            .Select(g => new SubtotalVendasDto(g.Count(), g.Sum(v => v.TotalBrl), g.Sum(v => v.LucroBrl))).FirstOrDefaultAsync(ct);
        return Results.Ok(new VendasPaginaDto(r.Items.Select(ToLista).ToList(), r.Page, r.PageSize, r.Total, fat ?? new SubtotalVendasDto(0, 0, 0)));
    }

    private static async Task<IResult> Imagens(string[]? nome, ICrivonftClient crivonft, IMemoryCache cache, ILogger<VendasLog> log, CancellationToken ct)
    {
        var nomes = (nome ?? []).Select(n => n.Trim()).Where(n => n.Length is > 0 and <= 200).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();
        var imagens = new Dictionary<string, string?>();
        var faltam = new List<string>();
        foreach (var n in nomes)
        {
            if (cache.TryGetValue("imagem-item:" + n.ToLowerInvariant(), out string? url)) imagens[n] = url; else faltam.Add(n);
        }
        if (faltam.Count > 0)
        {
            try
            {
                foreach (var (n, url) in await crivonft.ImagensDeItensAsync(faltam, ct))
                {
                    imagens[n] = url;
                    // Achou: guarda por 6h. Não achou: 5 min, para a foto aparecer logo depois de cadastrada.
                    cache.Set("imagem-item:" + n.ToLowerInvariant(), url, url is null ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(6));
                }
            }
            catch (CrivonftException e)
            {
                log.LogWarning("Imagens dos itens indisponíveis: {Mensagem}", e.Message);
                foreach (var n in faltam) imagens.TryAdd(n, null);
            }
        }
        return Results.Ok(new { imagens });
    }

    private static async Task<IResult> Detalhe(Guid id, AppDbContext db, CancellationToken ct)
    {
        var v = await db.Vendas.AsNoTracking().Include(x => x.Cliente).Include(x => x.Itens).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Venda não encontrada.");
        var origem = Nomes.Origem(v.Origem);
        VendaManualDto? manual = null;
        if (v.Manual)
        {
            var movs = await db.MovimentosEstoque.AsNoTracking().Where(m => m.VendaId == v.Id).OrderBy(m => m.CriadoEm).ToListAsync(ct);
            manual = new VendaManualDto(v.OrigemCusto is { } oc ? Nomes.Custo(oc) : null, v.QuantidadeEsmeraldas, v.UsaTaxaPrata, v.DolarCompraUsado, v.Observacao,
                movs.Select(m => new MovimentoVendaDto(Infrastructure.Estoque.EstoqueService.Nome(m.Tipo), m.Quantidade, m.CustoUnitario,
                    Estoque.EstoqueEndpoints.Motivo(m.Motivo), m.CriadoEm)).ToList());
        }
        var eventos = await db.EventosRecebidos.AsNoTracking().Where(e => e.Origem == origem && e.OrigemId == v.OrigemId)
            .OrderByDescending(e => e.OcorridoEm).ThenByDescending(e => e.RecebidoEm).Take(100).ToListAsync(ct);
        return Results.Ok(new VendaDetalheDto(ToLista(v),
            new ValoresDto(v.TotalBrl, v.DescontoBrl, v.TaxaBrl, v.ValorPixBrl, v.ValorCambios, v.CustoBrl, v.CustoUsd, v.CustoCambios, v.LucroBrl, v.MargemPercent),
            v.CupomCodigo is null ? null : new CupomDto(v.CupomCodigo, v.CupomTipo is { } t ? Nomes.Cupom(t) : null),
            v.Itens.OrderBy(i => i.Ordem).Select(i => new VendaItemDto(i.Ordem, i.Descricao, i.Quantidade, i.PrecoUsd, i.PrecoBrl, i.Referencia)).ToList(),
            v.AtualizadoEmOrigem, v.RemovidaEm,
            eventos.Select(e => new VendaEventoDto(e.Id, e.EventoId, e.Tipo, IntegracoesEndpoints.Nome(e.Estado), e.OcorridoEm, e.RecebidoEm, e.Resultado, e.Erro)).ToList(),
            manual));
    }

    private static async Task<IResult> Resumo(AppDbContext db, DateOnly? de, DateOnly? ate, string? origem, CancellationToken ct)
    {
        var p = Periodo(de, ate, maxDias: 400);
        if (p.Erro is not null) return p.Erro;
        var (ini, fim) = FusoSp.Intervalo(p.De, p.Ate);
        var q = db.Vendas.AsNoTracking().Where(VendaConsultas.Faturavel).Where(v => v.DataHora >= ini && v.DataHora < fim);
        if (!string.IsNullOrWhiteSpace(origem))
        {
            if (!Nomes.TentarOrigem(origem, out var o)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["origem"] = ["Use nft, cambios, site ou manual."] });
            q = q.Where(v => v.Origem == o);
        }
        // O agrupamento por dia é feito em memória para respeitar o fuso de São Paulo, só com as colunas necessárias.
        var linhas = await q.Select(v => new { v.DataHora, v.Origem, v.TotalBrl, v.LucroBrl }).ToListAsync(ct);

        var porDia = linhas.GroupBy(l => FusoSp.Dia(l.DataHora)).ToDictionary(g => g.Key, g => (Fat: g.Sum(x => x.TotalBrl), Luc: g.Sum(x => x.LucroBrl), N: g.Count()));
        var dias = new List<ResumoDiaDto>();
        for (var d = p.De; d <= p.Ate; d = d.AddDays(1))
            dias.Add(porDia.TryGetValue(d, out var x) ? new ResumoDiaDto(d, x.Fat, x.Luc, x.N) : new ResumoDiaDto(d, 0, 0, 0));

        var fat = linhas.Sum(l => l.TotalBrl);
        var n = linhas.Count;
        var porOrigem = Enum.GetValues<OrigemVenda>().Select(o =>
        {
            var l = linhas.Where(x => x.Origem == o).ToList();
            return new ResumoOrigemDto(Nomes.Origem(o), l.Sum(x => x.TotalBrl), l.Sum(x => x.LucroBrl), l.Count);
        }).ToList();
        return Results.Ok(new ResumoVendasDto(p.De, p.Ate,
            new TotaisDto(fat, linhas.Sum(l => l.LucroBrl), n == 0 ? 0 : Math.Round(fat / n, 2, MidpointRounding.AwayFromZero), n), porOrigem, dias));
    }
}
