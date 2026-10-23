using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public static class Paging
{
    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 20, 1, 100));

    public static async Task<PagedResult<T>> ToPageAsync<T>(this IQueryable<T> query, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<T>(items, p, size, total);
    }

    public static bool Descending(string? dir) => !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
}
