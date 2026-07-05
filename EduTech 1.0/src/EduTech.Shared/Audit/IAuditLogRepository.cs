namespace EduTech.Shared.Audit;

/// <summary>Append-only, tenant-scoped audit trail (writes bind @SchoolId + actor from the request context).</summary>
public interface IAuditLogRepository
{
    Task InsertAsync(string action, string entityType, Guid entityId, string summary, string? metadata,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditLogEntry>> ListAsync(string? entityType, Guid? entityId, int offset, int limit,
        CancellationToken cancellationToken);
}

public sealed class AuditLogEntry
{
    public Guid Id { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ActorType { get; init; }
    public string Action { get; init; } = string.Empty;
    public string? EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? Metadata { get; init; }
    public DateTime CreatedAt { get; init; }
}
