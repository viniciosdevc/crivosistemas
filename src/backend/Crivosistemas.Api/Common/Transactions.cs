using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Common;

public static class TransactionExtensions
{
    /// <summary>Executa o trabalho numa transação READ COMMITTED, compatível com a estratégia de nova tentativa da conexão.
    /// READ COMMITTED importa: depois de travar uma linha, as leituras seguintes precisam enxergar o que outras transações
    /// confirmaram (o REPEATABLE READ padrão do MySQL continuaria servindo o snapshot de antes do lock).</summary>
    public static Task<T> InTransactionAsync<T>(this AppDbContext db, Func<Task<T>> work, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });

    public static bool IsUniqueViolation(this DbUpdateException e) =>
        e.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true;
}
