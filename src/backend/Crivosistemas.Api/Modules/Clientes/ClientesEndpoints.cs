using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Vendas;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Clientes;

public sealed record ClienteListaDto(Guid Id, string Nickname, string? Email, decimal TotalGasto, int Compras, DateTime? UltimaCompra);
public sealed record ClienteDetalheDto(ClienteListaDto Cliente, string Chave, string? IdentidadeId, DateTime? CriadoEmOrigem, IReadOnlyList<VendaListaDto> Vendas);

/// <summary>Clientes vindos do crivonft. Total gasto, nº de compras e última compra contam só vendas faturáveis (pagas ou concluídas, não removidas).</summary>
public static class ClientesEndpoints
{
    public static void MapClientesEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/clientes").WithTags("Clientes").RequireAuthorization();
        g.MapGet("", Listar).WithSummary("Lista paginada com busca (nickname, e-mail) e ordenação por nome, totalGasto, compras ou ultimaCompra.");
        g.MapGet("/{id:guid}", Detalhe).WithSummary("Detalhe do cliente com o histórico de vendas.");
    }

    private static async Task<IResult> Listar(AppDbContext db, string? busca, string? ordem, string? dir, int? page, int? pageSize, CancellationToken ct)
    {
        var q = db.Clientes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim();
            q = q.Where(c => c.Nickname.Contains(b) || c.Chave.Contains(b) || (c.Email != null && c.Email.Contains(b)));
        }
        var proj = q.Select(c => new
        {
            c.Id, c.Nickname, c.Email,
            Total = db.Vendas.Where(v => v.ClienteId == c.Id).Where(VendaConsultas.Faturavel).Sum(v => (decimal?)v.TotalBrl) ?? 0,
            Compras = db.Vendas.Where(v => v.ClienteId == c.Id).Where(VendaConsultas.Faturavel).Count(),
            Ultima = db.Vendas.Where(v => v.ClienteId == c.Id).Where(VendaConsultas.Faturavel).Max(v => (DateTime?)v.DataHora),
        });
        var desc = Paging.Descending(dir);
        var ordenado = (ordem?.ToLowerInvariant()) switch
        {
            "totalgasto" => desc ? proj.OrderByDescending(x => x.Total) : proj.OrderBy(x => x.Total),
            "compras" => desc ? proj.OrderByDescending(x => x.Compras) : proj.OrderBy(x => x.Compras),
            "ultimacompra" => desc ? proj.OrderByDescending(x => x.Ultima) : proj.OrderBy(x => x.Ultima),
            _ => proj.OrderBy(x => x.Nickname),
        };
        var r = await ordenado.ThenBy(x => x.Id).ToPageAsync(page, pageSize, ct);
        return Results.Ok(new PagedResult<ClienteListaDto>(r.Items.Select(x => new ClienteListaDto(x.Id, x.Nickname, x.Email, x.Total, x.Compras, x.Ultima)).ToList(), r.Page, r.PageSize, r.Total));
    }

    private static async Task<IResult> Detalhe(Guid id, AppDbContext db, CancellationToken ct)
    {
        var c = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Cliente não encontrado.");
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Cliente).Include(v => v.Itens).AsSplitQuery().Where(v => v.ClienteId == id && v.RemovidaEm == null)
            .OrderByDescending(v => v.DataHora).Take(500).ToListAsync(ct);
        var fat = vendas.Where(v => v.Faturavel).ToList();
        var resumo = new ClienteListaDto(c.Id, c.Nickname, c.Email, fat.Sum(v => v.TotalBrl), fat.Count, fat.Count == 0 ? null : fat.Max(v => v.DataHora));
        return Results.Ok(new ClienteDetalheDto(resumo, c.Chave, c.IdentidadeId, c.CriadoEmOrigem, vendas.Select(VendasEndpoints.ToLista).ToList()));
    }
}
