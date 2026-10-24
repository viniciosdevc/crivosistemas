using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Domain.Estoque;
using Crivosistemas.Infrastructure.Estoque;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Estoque;

public sealed record ResumoEstoquesDto(ResumoEstoque Usd, ResumoEstoque Esmeraldas);
public sealed record LoteDto(Guid Id, string Tipo, string Origem, DateOnly DataCompra, decimal Quantidade, decimal Saldo, decimal CustoTotal, decimal CustoUnitario,
    string? Observacao, DateTime CriadoEm, DateTime? CanceladoEm, string? MotivoCancelamento, bool Editavel);
public sealed record MovimentoDto(Guid Id, string Tipo, Guid LoteId, decimal Quantidade, decimal CustoUnitario, string Motivo, Guid? VendaId, bool Estornado,
    string? Observacao, string? Usuario, DateTime CriadoEm);

/// <summary>Para USD: quantidade = US$ recebido, custoTotal = R$ depositado. Para esmeraldas: quantidade = esmeraldas, custoTotal = US$ pago.</summary>
public sealed record CompraRequest(
    [Required(ErrorMessage = "Informe a data da compra.")] DateOnly? Data,
    [Range(typeof(decimal), "0.00000001", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "A quantidade precisa ser maior que zero.")] decimal Quantidade,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O custo não pode ser negativo.")] decimal CustoTotal,
    [StringLength(300, ErrorMessage = "Observação muito longa.")] string? Observacao);
public sealed record CancelarRequest([StringLength(300, ErrorMessage = "Motivo muito longo.")] string? Motivo);
public sealed record AjusteRequest(
    [Required(ErrorMessage = "Informe se é entrada ou saída.")] string Direcao,
    [Range(typeof(decimal), "0.00000001", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O valor do ajuste precisa ser maior que zero.")] decimal Quantidade,
    decimal? CustoUnitario,
    [StringLength(300, ErrorMessage = "Motivo muito longo.")] string? Motivo);
public sealed record DefinirSaldoRequest(
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Informe um saldo maior ou igual a zero.")] decimal Saldo,
    decimal? CustoUnitario,
    [StringLength(300, ErrorMessage = "Motivo muito longo.")] string? Motivo);
public sealed record CustoMedioFixoRequest(decimal? Valor);

/// <summary>Estoque de dólar (compras de USD e custo médio) e de esmeraldas. Admin e operador lançam compras e ajustes;
/// fixar o custo médio e importar do crivonft são de administrador.</summary>
public static class EstoqueEndpoints
{
    public static void MapEstoqueEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/estoque").WithTags("Estoque").RequireAuthorization();
        g.MapGet("/resumo", Resumo).WithSummary("Saldo e custo médio do dólar e das esmeraldas.");
        g.MapGet("/{tipo}/lotes", Lotes).WithSummary("Lotes (compras e ajustes), mais recentes primeiro.");
        g.MapGet("/{tipo}/movimentos", Movimentos).WithSummary("Entradas e saídas, mais recentes primeiro.");
        g.MapPost("/{tipo}/compras", Comprar).WithSummary("Registra uma compra (lote novo).");
        g.MapPut("/{tipo}/compras/{id:guid}", Editar).WithSummary("Corrige uma compra da qual nada saiu ainda.");
        g.MapPost("/{tipo}/compras/{id:guid}/cancelar", Cancelar).WithSummary("Cancela uma compra da qual nada saiu ainda (fica no histórico).");
        g.MapPost("/{tipo}/ajustes", Ajustar).WithSummary("Ajuste manual de entrada (lote novo) ou saída (proporcional).");
        g.MapPost("/{tipo}/saldo", DefinirSaldo).WithSummary("Define o saldo pelo valor real e lança a diferença.");
        g.MapPut("/usd/custo-medio-fixo", FixarCustoMedio).RequireAuthorization(Session.AdminPolicy)
            .WithSummary("Fixa o custo médio do dólar (ou volta ao calculado com valor nulo).");
        g.MapPost("/importar-crivonft", Importar).RequireAuthorization(Session.AdminPolicy)
            .WithSummary("Traz os lotes com saldo do crivonft (idempotente pelo id de origem).");
    }

    private static TipoEstoque Tipo(string tipo) =>
        EstoqueService.TentarTipo(tipo, out var t) ? t : throw new NotFoundException("Estoque inexistente. Use usd ou esmeraldas.");

    private static LoteDto ToDto(LoteEstoque l) => new(l.Id, EstoqueService.Nome(l.Tipo), l.Origem switch
    {
        OrigemLote.Compra => "compra", OrigemLote.Ajuste => "ajuste", _ => "importacao_crivonft",
    }, l.DataCompra, l.Quantidade, l.Saldo, l.CustoTotal, l.CustoUnitario, l.Observacao, l.CriadoEm, l.CanceladoEm, l.MotivoCancelamento, !l.Cancelado && l.Intocado);

    internal static string Motivo(MotivoMovimento m) => m switch
    {
        MotivoMovimento.Compra => "compra", MotivoMovimento.Venda => "venda", MotivoMovimento.EstornoVenda => "estorno_venda", MotivoMovimento.Ajuste => "ajuste",
        MotivoMovimento.Cancelamento => "cancelamento", MotivoMovimento.Importacao => "importacao", _ => "edicao",
    };

    private static async Task<IResult> Resumo(EstoqueService svc, CancellationToken ct) =>
        Results.Ok(new ResumoEstoquesDto(await svc.ResumoAsync(TipoEstoque.Usd, ct), await svc.ResumoAsync(TipoEstoque.Esmeralda, ct)));

    private static async Task<IResult> Lotes(string tipo, AppDbContext db, bool? incluirCancelados, bool? comSaldo, int? page, int? pageSize, CancellationToken ct)
    {
        var t = Tipo(tipo);
        var q = db.LotesEstoque.AsNoTracking().Where(l => l.Tipo == t);
        if (incluirCancelados != true) q = q.Where(l => l.CanceladoEm == null);
        if (comSaldo == true) q = q.Where(l => l.Saldo > 0);
        var r = await q.OrderByDescending(l => l.DataCompra).ThenByDescending(l => l.CriadoEm).ToPageAsync(page, pageSize, ct);
        return Results.Ok(new PagedResult<LoteDto>(r.Items.Select(ToDto).ToList(), r.Page, r.PageSize, r.Total));
    }

    private static async Task<IResult> Movimentos(string tipo, AppDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var t = Tipo(tipo);
        var r = await db.MovimentosEstoque.AsNoTracking().Where(m => m.Tipo == t).OrderByDescending(m => m.CriadoEm).ThenByDescending(m => m.Id)
            .ToPageAsync(page, pageSize, ct);
        var userIds = r.Items.Where(m => m.UserId != null).Select(m => m.UserId!.Value).Distinct().ToList();
        var nomes = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return Results.Ok(new PagedResult<MovimentoDto>(r.Items.Select(m => new MovimentoDto(m.Id, EstoqueService.Nome(m.Tipo), m.LoteId, m.Quantidade, m.CustoUnitario,
            Motivo(m.Motivo), m.VendaId, m.EstornadoEm != null, m.Observacao, m.UserId is { } u && nomes.TryGetValue(u, out var n) ? n : null, m.CriadoEm)).ToList(),
            r.Page, r.PageSize, r.Total));
    }

    private static async Task<IResult> Comprar(string tipo, [FromBody] CompraRequest req, EstoqueService svc, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var t = Tipo(tipo);
        var lote = await db.InTransactionAsync(async () =>
        {
            await svc.TravarAsync(ct);
            var l = await svc.RegistrarCompraAsync(t, req.Data!.Value, req.Quantidade, req.CustoTotal, req.Observacao, http.User.Id(), ct);
            db.Audit(http, "estoque.compra", "LoteEstoque", l.Id, new { tipo = EstoqueService.Nome(t), req.Quantidade, req.CustoTotal, data = req.Data });
            await db.SaveChangesAsync(ct);
            return l;
        }, ct);
        return Results.Created($"/api/estoque/{EstoqueService.Nome(t)}/lotes/{lote.Id}", ToDto(lote));
    }

    private static async Task<IResult> Editar(string tipo, Guid id, [FromBody] CompraRequest req, EstoqueService svc, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var t = Tipo(tipo);
        var lote = await db.InTransactionAsync(async () =>
        {
            await svc.TravarAsync(ct);
            var l = await svc.EditarCompraAsync(t, id, req.Data!.Value, req.Quantidade, req.CustoTotal, req.Observacao, http.User.Id(), ct);
            db.Audit(http, "estoque.compra_editada", "LoteEstoque", l.Id, new { req.Quantidade, req.CustoTotal, data = req.Data });
            await db.SaveChangesAsync(ct);
            return l;
        }, ct);
        return Results.Ok(ToDto(lote));
    }

    private static async Task<IResult> Cancelar(string tipo, Guid id, [FromBody] CancelarRequest? req, EstoqueService svc, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var t = Tipo(tipo);
        var lote = await db.InTransactionAsync(async () =>
        {
            await svc.TravarAsync(ct);
            var l = await svc.CancelarCompraAsync(t, id, req?.Motivo ?? "", http.User.Id(), ct);
            db.Audit(http, "estoque.compra_cancelada", "LoteEstoque", l.Id, new { motivo = req?.Motivo });
            await db.SaveChangesAsync(ct);
            return l;
        }, ct);
        return Results.Ok(ToDto(lote));
    }

    private static async Task<IResult> Ajustar(string tipo, [FromBody] AjusteRequest req, EstoqueService svc, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var t = Tipo(tipo);
        var entrada = req.Direcao?.Trim().ToLowerInvariant() switch
        {
            "entrada" => true, "saida" or "saída" => false,
            _ => throw new Domain.Common.DomainException("direcao_invalida", "Use entrada ou saida."),
        };
        await db.InTransactionAsync(async () =>
        {
            await svc.TravarAsync(ct);
            await svc.AjustarAsync(t, entrada, req.Quantidade, req.CustoUnitario, req.Motivo ?? "", http.User.Id(), ct);
            db.Audit(http, "estoque.ajuste", "Estoque", null, new { tipo = EstoqueService.Nome(t), entrada, req.Quantidade, req.CustoUnitario, req.Motivo });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
        return Results.Ok(await svc.ResumoAsync(t, ct));
    }

    private static async Task<IResult> DefinirSaldo(string tipo, [FromBody] DefinirSaldoRequest req, EstoqueService svc, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var t = Tipo(tipo);
        await db.InTransactionAsync(async () =>
        {
            await svc.TravarAsync(ct);
            var delta = await svc.DefinirSaldoAsync(t, req.Saldo, req.CustoUnitario, req.Motivo ?? "", http.User.Id(), ct);
            db.Audit(http, "estoque.saldo_definido", "Estoque", null, new { tipo = EstoqueService.Nome(t), req.Saldo, delta, req.Motivo });
            await db.SaveChangesAsync(ct);
            return delta;
        }, ct);
        return Results.Ok(await svc.ResumoAsync(t, ct));
    }

    private static async Task<IResult> FixarCustoMedio([FromBody] CustoMedioFixoRequest req, EstoqueService svc, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        await db.InTransactionAsync(async () =>
        {
            await svc.TravarAsync(ct);
            await svc.FixarCustoMedioUsdAsync(req.Valor, ct);
            db.Audit(http, "estoque.custo_medio_fixo", "Estoque", null, new { valor = req.Valor });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
        return Results.Ok(await svc.ResumoAsync(TipoEstoque.Usd, ct));
    }

    private static async Task<IResult> Importar(EstoqueService svc, ICrivonftClient crivonft, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!crivonft.ExportConfigurado)
            return Results.Json(new { title = "A integração com o crivonft não está configurada.", code = "integracao_nao_configurada", status = 503 }, statusCode: 503);
        try
        {
            var r = await db.InTransactionAsync(async () =>
            {
                await svc.TravarAsync(ct);
                var res = await svc.ImportarDoCrivonftAsync(http.User.Id(), ct);
                db.Audit(http, "estoque.importacao_crivonft", "Estoque", null, res);
                await db.SaveChangesAsync(ct);
                return res;
            }, ct);
            return Results.Ok(r);
        }
        catch (CrivonftException e)
        {
            return Results.Json(new { title = e.Message, code = "crivonft_indisponivel", status = 502 }, statusCode: 502);
        }
    }
}
