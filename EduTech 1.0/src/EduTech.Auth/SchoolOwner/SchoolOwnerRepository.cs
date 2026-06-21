using System.Data;
using EduTech.Shared.Persistence;

namespace EduTech.Auth.SchoolOwner;

/// <summary>
/// Data access for <c>school_owners</c> (the legal proprietor account). Global identity table —
/// keyed by phone, derives from <see cref="BaseRepository"/>.
/// </summary>
internal interface ISchoolOwnerRepository
{
    Task<bool> ExistsByPhoneAsync(string phone, CancellationToken cancellationToken);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(Guid schoolId, string fullName, string phone, string? email,
        string passwordHash, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The owner id for a phone, or null if no account exists.</summary>
    Task<Guid?> GetIdByPhoneAsync(string phone, CancellationToken cancellationToken);

    Task MarkPhoneVerifiedAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Everything needed to authenticate a login by phone. Null if no account exists.</summary>
    Task<SchoolOwnerLoginRow?> GetByPhoneForLoginAsync(string phone, CancellationToken cancellationToken);

    /// <summary>Records a failed-login count and optional lockout window.</summary>
    Task SetLoginFailureAsync(Guid ownerId, int failedCount, DateTime? lockedUntil,
        CancellationToken cancellationToken);

    /// <summary>Clears failure count + lockout after a successful login.</summary>
    Task ClearLoginFailuresAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Current fields needed to re-mint an access token on refresh. Null if not found.</summary>
    Task<SchoolOwnerTokenRow?> GetTokenClaimsAsync(Guid ownerId, CancellationToken cancellationToken);

    Task SetPasswordAsync(Guid ownerId, string passwordHash, CancellationToken cancellationToken);

    /// <summary>Profile for GET /me (owner joined with the school). Null if not found.</summary>
    Task<SchoolOwnerProfileRow?> GetProfileAsync(Guid ownerId, CancellationToken cancellationToken);
}

/// <summary>Authentication fields read during login.</summary>
internal sealed class SchoolOwnerLoginRow
{
    public Guid Id { get; init; }
    public Guid SchoolId { get; init; }
    public string PasswordHash { get; init; } = string.Empty;
    public bool PhoneVerified { get; init; }
    public bool IsActive { get; init; }
    public int FailedLoginCount { get; init; }
    public DateTime? LockedUntil { get; init; }
}

/// <summary>Fields re-read on refresh to rebuild the access token (propagates status changes).</summary>
internal sealed class SchoolOwnerTokenRow
{
    public Guid SchoolId { get; init; }
    public string Phone { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

/// <summary>Owner profile for GET /me (joined with the school).</summary>
internal sealed class SchoolOwnerProfileRow
{
    public string FullName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public bool PhoneVerified { get; init; }
    public Guid SchoolId { get; init; }
    public string SchoolStatus { get; init; } = string.Empty;
    public string KycStatus { get; init; } = string.Empty;
    public string? Subdomain { get; init; }
}

internal sealed class SchoolOwnerRepository : BaseRepository, ISchoolOwnerRepository
{
    public SchoolOwnerRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<bool> ExistsByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM school_owners WHERE phone = @Phone",
            new { Phone = phone }, cancellationToken);
        return count > 0;
    }

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM school_owners WHERE email = @Email",
            new { Email = email }, cancellationToken);
        return count > 0;
    }

    public async Task<Guid> CreateAsync(Guid schoolId, string fullName, string phone, string? email,
        string passwordHash, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO school_owners (school_id, full_name, phone, email, password_hash)
            VALUES (@SchoolId, @FullName, @Phone, @Email, @PasswordHash)
            RETURNING id
            """,
            new
            {
                SchoolId = schoolId,
                FullName = fullName,
                Phone = phone,
                Email = email,
                PasswordHash = passwordHash
            },
            cancellationToken, transaction);
    }

    public Task<Guid?> GetIdByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM school_owners WHERE phone = @Phone",
            new { Phone = phone }, cancellationToken);
    }

    public Task MarkPhoneVerifiedAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE school_owners SET phone_verified = TRUE, updated_at = NOW() WHERE id = @Id",
            new { Id = ownerId }, cancellationToken);
    }

    public Task<SchoolOwnerLoginRow?> GetByPhoneForLoginAsync(string phone, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<SchoolOwnerLoginRow>(
            """
            SELECT id, school_id, password_hash, phone_verified, is_active, failed_login_count, locked_until
            FROM school_owners
            WHERE phone = @Phone
            """,
            new { Phone = phone }, cancellationToken);
    }

    public Task SetLoginFailureAsync(Guid ownerId, int failedCount, DateTime? lockedUntil,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE school_owners
            SET failed_login_count = @FailedCount, locked_until = @LockedUntil, updated_at = NOW()
            WHERE id = @Id
            """,
            new { Id = ownerId, FailedCount = failedCount, LockedUntil = lockedUntil }, cancellationToken);
    }

    public Task ClearLoginFailuresAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE school_owners SET failed_login_count = 0, locked_until = NULL, updated_at = NOW() WHERE id = @Id",
            new { Id = ownerId }, cancellationToken);
    }

    public Task<SchoolOwnerTokenRow?> GetTokenClaimsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<SchoolOwnerTokenRow>(
            "SELECT school_id, phone, is_active FROM school_owners WHERE id = @Id",
            new { Id = ownerId }, cancellationToken);
    }

    public Task SetPasswordAsync(Guid ownerId, string passwordHash, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE school_owners SET password_hash = @PasswordHash, updated_at = NOW() WHERE id = @Id",
            new { Id = ownerId, PasswordHash = passwordHash }, cancellationToken);
    }

    public Task<SchoolOwnerProfileRow?> GetProfileAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<SchoolOwnerProfileRow>(
            """
            SELECT o.full_name, o.phone, o.email, o.phone_verified,
                   o.school_id, s.status AS school_status, s.kyc_status, s.subdomain
            FROM school_owners o
            JOIN schools s ON s.id = o.school_id
            WHERE o.id = @Id
            """,
            new { Id = ownerId }, cancellationToken);
    }
}
