using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Shared.Audit;

/// <summary>
/// Writes/reads the per-school audit trail. Tenant-scoped via <see cref="TenantRepository"/> (@SchoolId is
/// bound from the JWT); the actor (who did it) is stamped from the same request context on insert.
/// </summary>
internal sealed class AuditLogRepository : TenantRepository, IAuditLogRepository
{
    private readonly IEduTechRequestContext _context;

    public AuditLogRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext context)
        : base(connectionFactory, context)
    {
        _context = context;
    }

    public Task InsertAsync(string action, string entityType, Guid entityId, string summary, string? metadata,
        CancellationToken cancellationToken)
    {
        Guid? actorId = Guid.TryParse(_context.UserId, out Guid parsed) ? parsed : null;

        return ExecuteAsync(
            """
            INSERT INTO audit_logs (school_id, actor_user_id, actor_type, action, entity_type, entity_id, summary, metadata)
            VALUES (@SchoolId, @ActorId, @ActorType, @Action, @EntityType, @EntityId, @Summary, @Metadata::jsonb)
            """,
            TenantParameters(new
            {
                ActorId = actorId,
                ActorType = _context.UserType,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Summary = summary,
                Metadata = metadata
            }),
            cancellationToken);
    }

    public Task<IReadOnlyList<AuditLogEntry>> ListAsync(string? entityType, Guid? entityId, int offset, int limit,
        CancellationToken cancellationToken)
    {
        return QueryAsync<AuditLogEntry>(
            """
            SELECT id, actor_user_id AS ActorUserId, actor_type AS ActorType, action,
                   entity_type AS EntityType, entity_id AS EntityId, summary,
                   metadata::text AS Metadata, created_at AS CreatedAt
            FROM audit_logs
            WHERE school_id = @SchoolId
              AND (@EntityType IS NULL OR entity_type = @EntityType)
              AND (@EntityId IS NULL OR entity_id = @EntityId)
            ORDER BY created_at DESC
            OFFSET @Offset LIMIT @Limit
            """,
            TenantParameters(new { EntityType = entityType, EntityId = entityId, Offset = offset, Limit = limit }),
            cancellationToken);
    }
}
