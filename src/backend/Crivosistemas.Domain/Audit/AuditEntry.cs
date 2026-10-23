using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Audit;

/// <summary>Registro somente de inclusão das mudanças administrativas.</summary>
public class AuditEntry : Entity
{
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public Guid? EntityId { get; set; }
    /// <summary>JSON pequeno descrevendo a mudança. Nunca contém credenciais.</summary>
    public string? Data { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
