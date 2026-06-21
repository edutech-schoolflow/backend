using System.Data;
using EduTech.Shared.Persistence;

namespace EduTech.Auth.PlatformAdmin;

/// <summary>Append-only audit trail of platform-admin actions.</summary>
internal interface IAdminAuditLogRepository
{
    Task InsertAsync(Guid adminId, string action, string? targetType, Guid? targetId,
        string? metadataJson, string? ipAddress, IDbTransaction transaction, CancellationToken cancellationToken);
}

internal sealed class AdminAuditLogRepository : BaseRepository, IAdminAuditLogRepository
{
    public AdminAuditLogRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task InsertAsync(Guid adminId, string action, string? targetType, Guid? targetId,
        string? metadataJson, string? ipAddress, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO platform_admin_audit_log (admin_id, action, target_type, target_id, metadata, ip_address)
            VALUES (@AdminId, @Action, @TargetType, @TargetId, CAST(@Metadata AS jsonb), @IpAddress)
            """,
            new
            {
                AdminId = adminId,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Metadata = metadataJson,
                IpAddress = ipAddress
            },
            cancellationToken, transaction);
    }
}
