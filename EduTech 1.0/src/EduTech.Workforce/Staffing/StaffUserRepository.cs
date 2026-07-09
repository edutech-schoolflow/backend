using System.Data;
using EduTech.Shared.Persistence;

namespace EduTech.Workforce;

/// <summary>
/// Data access for <c>staff_users</c> — the global, standalone staff identity (keyed by phone).
/// No school_id (staff are platform-level), so this derives from <see cref="BaseRepository"/>.
/// </summary>
internal interface IStaffUserRepository
{
    Task<bool> ExistsByPhoneAsync(string phone, CancellationToken cancellationToken);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(string firstName, string? middleName, string lastName, string phone,
        string? email, string passwordHash, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a PENDING staff_user (no password yet) for someone invited by a school who has no
    /// account. They set a password + verify their phone when they accept. Runs in the invite transaction.
    /// </summary>
    Task<Guid> CreatePendingAsync(string firstName, string? middleName, string lastName, string phone,
        IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The staff id for a phone, or null if no account exists.</summary>
    Task<Guid?> GetIdByPhoneAsync(string phone, CancellationToken cancellationToken);

    Task MarkPhoneVerifiedAsync(Guid staffUserId, CancellationToken cancellationToken);

    Task<StaffUserLoginRow?> GetByPhoneForLoginAsync(string phone, CancellationToken cancellationToken);

    Task SetLoginFailureAsync(Guid staffUserId, int failedCount, DateTime? lockedUntil,
        CancellationToken cancellationToken);

    Task ClearLoginFailuresAsync(Guid staffUserId, CancellationToken cancellationToken);

    Task<StaffUserTokenRow?> GetTokenClaimsAsync(Guid staffUserId, CancellationToken cancellationToken);

    /// <summary>Account state used by the invite-accept flow to branch new-vs-existing account.</summary>
    Task<StaffAccountState?> GetAccountStateAsync(Guid staffUserId, CancellationToken cancellationToken);

    /// <summary>Sets the password and marks the phone verified — completes a pending (invited) account.</summary>
    Task CompleteAccountAsync(Guid staffUserId, string passwordHash, IDbTransaction transaction,
        CancellationToken cancellationToken);

    Task SetPasswordAsync(Guid staffUserId, string passwordHash, CancellationToken cancellationToken);

    /// <summary>Identity profile for GET /me. Null if not found.</summary>
    Task<StaffProfileRow?> GetProfileAsync(Guid staffUserId, CancellationToken cancellationToken);
}

/// <summary>Account state for the invite-accept branch.</summary>
internal sealed class StaffAccountState
{
    public string Phone { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string KycStatus { get; init; } = string.Empty;
    public bool HasPassword { get; init; }
    public bool PhoneVerified { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>Staff identity profile for GET /me.</summary>
internal sealed class StaffProfileRow
{
    public string FullName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public bool PhoneVerified { get; init; }
    public string KycStatus { get; init; } = string.Empty;
}

/// <summary>Authentication fields read during login.</summary>
internal sealed class StaffUserLoginRow
{
    public Guid Id { get; init; }
    public string? PasswordHash { get; init; }   // null until the account sets a password
    public bool PhoneVerified { get; init; }
    public bool IsActive { get; init; }
    public string KycStatus { get; init; } = string.Empty;
    public int FailedLoginCount { get; init; }
    public DateTime? LockedUntil { get; init; }
}

/// <summary>Fields re-read on refresh to rebuild the identity token.</summary>
internal sealed class StaffUserTokenRow
{
    public string Phone { get; init; } = string.Empty;
    public string KycStatus { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

internal sealed class StaffUserRepository : BaseRepository, IStaffUserRepository
{
    public StaffUserRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<bool> ExistsByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM staff_users WHERE phone = @Phone",
            new { Phone = phone }, cancellationToken);
        return count > 0;
    }

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM staff_users WHERE email = @Email",
            new { Email = email }, cancellationToken);
        return count > 0;
    }

    public async Task<Guid> CreateAsync(string firstName, string? middleName, string lastName, string phone,
        string? email, string passwordHash, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO staff_users (first_name, middle_name, last_name, phone, email, password_hash)
            VALUES (@FirstName, @MiddleName, @LastName, @Phone, @Email, @PasswordHash)
            RETURNING id
            """,
            new { FirstName = firstName, MiddleName = middleName, LastName = lastName, Phone = phone, Email = email, PasswordHash = passwordHash },
            cancellationToken);
    }

    public async Task<Guid> CreatePendingAsync(string firstName, string? middleName, string lastName, string phone,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<Guid>(
            "INSERT INTO staff_users (first_name, middle_name, last_name, phone) VALUES (@FirstName, @MiddleName, @LastName, @Phone) RETURNING id",
            new { FirstName = firstName, MiddleName = middleName, LastName = lastName, Phone = phone }, cancellationToken, transaction);
    }

    public Task<Guid?> GetIdByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM staff_users WHERE phone = @Phone",
            new { Phone = phone }, cancellationToken);
    }

    public Task MarkPhoneVerifiedAsync(Guid staffUserId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE staff_users SET phone_verified = TRUE, updated_at = NOW() WHERE id = @Id",
            new { Id = staffUserId }, cancellationToken);
    }

    public Task<StaffUserLoginRow?> GetByPhoneForLoginAsync(string phone, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StaffUserLoginRow>(
            """
            SELECT id, password_hash, phone_verified, is_active, kyc_status, failed_login_count, locked_until
            FROM staff_users
            WHERE phone = @Phone
            """,
            new { Phone = phone }, cancellationToken);
    }

    public Task SetLoginFailureAsync(Guid staffUserId, int failedCount, DateTime? lockedUntil,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE staff_users
            SET failed_login_count = @FailedCount, locked_until = @LockedUntil, updated_at = NOW()
            WHERE id = @Id
            """,
            new { Id = staffUserId, FailedCount = failedCount, LockedUntil = lockedUntil }, cancellationToken);
    }

    public Task ClearLoginFailuresAsync(Guid staffUserId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE staff_users SET failed_login_count = 0, locked_until = NULL, updated_at = NOW() WHERE id = @Id",
            new { Id = staffUserId }, cancellationToken);
    }

    public Task<StaffUserTokenRow?> GetTokenClaimsAsync(Guid staffUserId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StaffUserTokenRow>(
            "SELECT phone, kyc_status, is_active FROM staff_users WHERE id = @Id",
            new { Id = staffUserId }, cancellationToken);
    }

    public Task<StaffAccountState?> GetAccountStateAsync(Guid staffUserId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StaffAccountState>(
            """
            SELECT phone, first_name, last_name, kyc_status,
                   (password_hash IS NOT NULL) AS has_password, phone_verified, is_active
            FROM staff_users
            WHERE id = @Id
            """,
            new { Id = staffUserId }, cancellationToken);
    }

    public Task CompleteAccountAsync(Guid staffUserId, string passwordHash, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE staff_users
            SET password_hash = @PasswordHash, phone_verified = TRUE, updated_at = NOW()
            WHERE id = @Id
            """,
            new { Id = staffUserId, PasswordHash = passwordHash }, cancellationToken, transaction);
    }

    public Task SetPasswordAsync(Guid staffUserId, string passwordHash, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE staff_users SET password_hash = @PasswordHash, updated_at = NOW() WHERE id = @Id",
            new { Id = staffUserId, PasswordHash = passwordHash }, cancellationToken);
    }

    public Task<StaffProfileRow?> GetProfileAsync(Guid staffUserId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StaffProfileRow>(
            "SELECT concat_ws(' ', first_name, middle_name, last_name) AS full_name, phone, email, phone_verified, kyc_status FROM staff_users WHERE id = @Id",
            new { Id = staffUserId }, cancellationToken);
    }
}
