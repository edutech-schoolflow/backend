using EduTech.Shared.Persistence;

namespace EduTech.Auth.PlatformAdmin;

internal interface IPlatformAdminRepository
{
    Task<bool> ExistsAnyAsync(CancellationToken cancellationToken);

    Task<Guid> CreateAsync(string fullName, string email, string passwordHash, string role,
        Guid? createdBy, CancellationToken cancellationToken);

    Task<PlatformAdminLoginRow?> GetByEmailForLoginAsync(string email, CancellationToken cancellationToken);

    Task SetLoginFailureAsync(Guid adminId, int failedCount, DateTime? lockedUntil, CancellationToken cancellationToken);

    Task ClearLoginFailuresAsync(Guid adminId, CancellationToken cancellationToken);
}

/// <summary>Fields read to authenticate an admin login.</summary>
internal sealed class PlatformAdminLoginRow
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int FailedLoginCount { get; init; }
    public DateTime? LockedUntil { get; init; }
}

internal sealed class PlatformAdminRepository : BaseRepository, IPlatformAdminRepository
{
    public PlatformAdminRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<bool> ExistsAnyAsync(CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM platform_admins", null, cancellationToken);
        return count > 0;
    }

    public Task<Guid> CreateAsync(string fullName, string email, string passwordHash, string role,
        Guid? createdBy, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO platform_admins (full_name, email, password_hash, role, created_by)
            VALUES (@FullName, @Email, @PasswordHash, @Role, @CreatedBy)
            RETURNING id
            """,
            new
            {
                FullName = fullName,
                Email = email,
                PasswordHash = passwordHash,
                Role = role,
                CreatedBy = createdBy
            },
            cancellationToken);
    }

    public Task<PlatformAdminLoginRow?> GetByEmailForLoginAsync(string email, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<PlatformAdminLoginRow>(
            """
            SELECT id, email, password_hash, role, is_active, failed_login_count, locked_until
            FROM platform_admins
            WHERE email = @Email
            """,
            new { Email = email }, cancellationToken);
    }

    public Task SetLoginFailureAsync(Guid adminId, int failedCount, DateTime? lockedUntil,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE platform_admins
            SET failed_login_count = @FailedCount, locked_until = @LockedUntil, updated_at = NOW()
            WHERE id = @Id
            """,
            new { Id = adminId, FailedCount = failedCount, LockedUntil = lockedUntil }, cancellationToken);
    }

    public Task ClearLoginFailuresAsync(Guid adminId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE platform_admins
            SET failed_login_count = 0, locked_until = NULL, last_login_at = NOW(), updated_at = NOW()
            WHERE id = @Id
            """,
            new { Id = adminId }, cancellationToken);
    }
}
