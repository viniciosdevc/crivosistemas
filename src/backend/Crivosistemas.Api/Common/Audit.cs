using System.Text.Json;
using Crivosistemas.Domain.Audit;
using Crivosistemas.Infrastructure.Persistence;

namespace Crivosistemas.Api.Common;

public static class AuditExtensions
{
    /// <summary>Adiciona um registro de auditoria à unidade de trabalho atual: ele confirma (ou desfaz) junto com a própria mudança.</summary>
    public static void Audit(this AppDbContext db, HttpContext? http, string action, string entityType, Guid? entityId, object? data = null)
    {
        Guid? userId = http?.User.Identity?.IsAuthenticated == true ? http.User.Id() : null;
        db.AuditEntries.Add(new AuditEntry
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Data = data is null ? null : JsonSerializer.Serialize(data, JsonSerializerOptions.Web),
        });
    }
}
