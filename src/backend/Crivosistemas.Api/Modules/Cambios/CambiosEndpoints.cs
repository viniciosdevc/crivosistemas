using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Domain.Cambios;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Cambios;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Cambios;

public sealed record MovimentoCambiosDto(Guid Id, string Tipo, decimal Quantidade, decimal SaldoAnterior, decimal SaldoNovo, decimal? ValorBrl, Guid? ClienteId,
    string? Nickname, Guid? VendaId, bool Estornado, string? Observacao, string? Usuario, DateTime DataHora);
public sealed record ResumoCambiosDto(decimal Saldo, decimal TotalVendidoQuantidade, decimal TotalVendidoBrl);

public sealed record VendaCambiosRequest(
    [Range(typeof(decimal), "0.01", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "A quantidade precisa ser maior que zero.")] decimal Quantidade,
    [Range(typeof(decimal), "0.01", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O valor em R$ precisa ser maior que zero.")] decimal ValorBrl,
    [StringLength(120, ErrorMessage = "Nickname muito longo.")] string? Nickname,
    DateTime? DataHora,
    [StringLength(300, ErrorMessage = "Observação muito longa.")] string? Observacao);
public sealed record DefinirSaldoCambiosRequest(
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Informe um saldo maior ou igual a zero.")] decimal Saldo,
    [StringLength(300, ErrorMessage = "Motivo muito longo.")] string? Motivo);
public sealed record EstornarCambiosRequest([StringLength(300, ErrorMessage = "Motivo muito longo.")] string? Motivo);

/// <summary>Saldo de câmbios e venda de câmbios por R$. O saldo cresce com as vendas manuais pagas em câmbios (só as criadas aqui) e pode ser definido à mão.</summary>
public static class CambiosEndpoints
{
    public static void MapCambiosEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cambios").WithTags("Câmbios").RequireAuthorization();
        g.MapGet("/resumo", Resumo).WithSummary("Saldo atual e total vendido.");
        g.MapGet("/movimentos", Movimentos).WithSummary("Livro do saldo (saldo anterior e novo), com filtro por cliente.");
        g.MapPost("/vendas", Vender).WithSummary("Vende câmbios por R$: debita o saldo.");
        g.MapPost("/saldo", DefinirSaldo).WithSummary("Define o saldo pelo valor real e lança a diferença.");
        g.MapPost("/movimentos/{id:guid}/estornar", Estornar).WithSummary("Estorna uma venda de câmbios ou um ajuste.");
    }

    internal static string Nome(TipoMovimentoCambios t) => t switch
    {
        TipoMovimentoCambios.Venda => "venda", TipoMovimentoCambios.VendaManual => "venda_manual", TipoMovimentoCambios.Ajuste => "ajuste", _ => "estorno",
    };

    private static async Task<IResult> Resumo(AppDbContext db, CambiosService svc, CancellationToken ct)
    {
        var vendas = db.MovimentosCambios.AsNoTracking().Where(m => m.Tipo == TipoMovimentoCambios.Venda && m.EstornadoEm == null);
        var qtd = await vendas.SumAsync(m => (decimal?)-m.Quantidade, ct) ?? 0;
        var brl = await vendas.SumAsync(m => m.ValorBrl, ct) ?? 0;
        return Results.Ok(new ResumoCambiosDto(await svc.ObterSaldoAsync(ct), qtd, brl));
    }

    private static async Task<IResult> Movimentos(AppDbContext db, Guid? clienteId, int? page, int? pageSize, CancellationToken ct)
    {
        var q = db.MovimentosCambios.AsNoTracking().AsQueryable();
        if (clienteId is { } c) q = q.Where(m => m.ClienteId == c);
        var r = await q.OrderByDescending(m => m.DataHora).ThenByDescending(m => m.Id).ToPageAsync(page, pageSize, ct);
        var cli = r.Items.Where(m => m.ClienteId != null).Select(m => m.ClienteId!.Value).Distinct().ToList();
        var nicks = await db.Clientes.AsNoTracking().Where(x => cli.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Nickname, ct);
        var uids = r.Items.Where(m => m.UserId != null).Select(m => m.UserId!.Value).Distinct().ToList();
        var nomes = await db.Users.AsNoTracking().Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return Results.Ok(new PagedResult<MovimentoCambiosDto>(r.Items.Select(m => new MovimentoCambiosDto(m.Id, Nome(m.Tipo), m.Quantidade, m.SaldoAnterior, m.SaldoNovo, m.ValorBrl,
            m.ClienteId, m.ClienteId is { } ci && nicks.TryGetValue(ci, out var n) ? n : null, m.VendaId, m.EstornadoEm != null, m.Observacao,
            m.UserId is { } u && nomes.TryGetValue(u, out var un) ? un : null, m.DataHora)).ToList(), r.Page, r.PageSize, r.Total));
    }

    private static async Task<IResult> Vender([FromBody] VendaCambiosRequest req, AppDbContext db, CambiosService svc, IngestaoService ingestao, HttpContext http, CancellationToken ct)
    {
        var qtd = Math.Round(req.Quantidade, 2, MidpointRounding.AwayFromZero);
        var m = await db.InTransactionAsync(async () =>
        {
            await svc.TravarAsync(ct);
            Cliente? cliente = null;
            var nick = req.Nickname?.Trim();
            if (!string.IsNullOrEmpty(nick)) cliente = await ingestao.AplicarClienteAsync(new ClientePayload(nick, nick, null, null, null), sobrescrever: false, ct);
            var mov = await svc.LancarAsync(TipoMovimentoCambios.Venda, -qtd, http.User.Id(), req.DataHora?.ToUniversalTime(), req.ValorBrl, cliente?.Id, observacao: req.Observacao, ct: ct);
            db.Audit(http, "cambios.venda", "MovimentoCambios", mov.Id, new { qtd, req.ValorBrl, nickname = nick });
            await db.SaveChangesAsync(ct);
            return mov;
        }, ct);
        return Results.Created($"/api/cambios/movimentos/{m.Id}", new { m.Id, m.SaldoAnterior, m.SaldoNovo });
    }

    private static async Task<IResult> DefinirSaldo([FromBody] DefinirSaldoCambiosRequest req, AppDbContext db, CambiosService svc, HttpContext http, CancellationToken ct)
    {
        var novo = Math.Round(req.Saldo, 2, MidpointRounding.AwayFromZero);
        var m = await db.InTransactionAsync(async () =>
        {
            await svc.TravarAsync(ct);
            var atual = await svc.ObterSaldoAsync(ct);
            if (novo == atual) throw new DomainException("saldo_igual", "O saldo informado é igual ao atual.");
            var mov = await svc.LancarAsync(TipoMovimentoCambios.Ajuste, novo - atual, http.User.Id(), observacao: req.Motivo, ct: ct);
            db.Audit(http, "cambios.saldo_definido", "MovimentoCambios", mov.Id, new { de = atual, para = novo, req.Motivo });
            await db.SaveChangesAsync(ct);
            return mov;
        }, ct);
        return Results.Ok(new { m.SaldoAnterior, m.SaldoNovo });
    }

    private static async Task<IResult> Estornar(Guid id, [FromBody] EstornarCambiosRequest? req, AppDbContext db, CambiosService svc, HttpContext http, CancellationToken ct)
    {
        var e = await db.InTransactionAsync(async () =>
        {
            await svc.TravarAsync(ct);
            var o = await db.MovimentosCambios.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new NotFoundException("Lançamento não encontrado.");
            if (o.Tipo is not (TipoMovimentoCambios.Venda or TipoMovimentoCambios.Ajuste))
                throw new DomainException("estorno_invalido", "Só vendas de câmbios e ajustes podem ser estornados. Venda manual se desfaz cancelando a venda.");
            var mov = await svc.EstornarAsync(o, http.User.Id(), req?.Motivo, ct);
            db.Audit(http, "cambios.estorno", "MovimentoCambios", o.Id, new { req?.Motivo });
            await db.SaveChangesAsync(ct);
            return mov;
        }, ct);
        return Results.Ok(new { e.SaldoAnterior, e.SaldoNovo });
    }
}
