using EduTech.Shared.Persistence;

namespace EduTech.Auth.Parent;

/// <summary>Data access for <c>parents</c> — the standalone, school-agnostic guardian account.</summary>
internal interface IParentRepository
{
    Task<bool> ExistsByPhoneAsync(string phone, CancellationToken cancellationToken);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(string firstName, string? middleName, string lastName, string phone,
        string? email, string passwordHash, CancellationToken cancellationToken);

    /// <summary>
    /// Claim state for a phone during registration: does a parent with this phone exist, and if so has it
    /// set a password? A school-seeded parent exists with no password (unclaimed) — registering claims it.
    /// Null when the phone is free.
    /// </summary>
    Task<ParentClaimState?> GetClaimStateByPhoneAsync(string phone, CancellationToken cancellationToken);

    /// <summary>
    /// Claims a pending, password-less parent: sets the password and adopts the registrant's own name/email.
    /// Guarded on <c>password_hash IS NULL</c> so a concurrent claim can't overwrite a real account; returns
    /// the number of rows affected (0 = already claimed).
    /// </summary>
    Task<int> ClaimAsync(Guid parentId, string firstName, string? middleName, string lastName,
        string? email, string passwordHash, CancellationToken cancellationToken);

    Task<Guid?> GetIdByPhoneAsync(string phone, CancellationToken cancellationToken);

    /// <summary>Marks the phone verified and activates the account (pending → active).</summary>
    Task MarkPhoneVerifiedAsync(Guid parentId, CancellationToken cancellationToken);

    Task<ParentLoginRow?> GetByPhoneForLoginAsync(string phone, CancellationToken cancellationToken);

    Task SetLoginFailureAsync(Guid parentId, int failedCount, DateTime? lockedUntil, CancellationToken cancellationToken);
    Task ClearLoginFailuresAsync(Guid parentId, CancellationToken cancellationToken);

    Task<ParentTokenRow?> GetTokenClaimsAsync(Guid parentId, CancellationToken cancellationToken);

    /// <summary>Sets/replaces the 6-digit payment PIN hash (clears any PIN lockout).</summary>
    Task SetPaymentPinAsync(Guid parentId, string pinHash, CancellationToken cancellationToken);

    Task SetPasswordAsync(Guid parentId, string passwordHash, CancellationToken cancellationToken);

    /// <summary>Profile for GET /me. Null if not found.</summary>
    Task<ParentProfileRow?> GetProfileAsync(Guid parentId, CancellationToken cancellationToken);
}

public sealed class ParentClaimState
{
    public Guid Id { get; init; }
    public bool HasPassword { get; init; }
}

internal sealed class ParentLoginRow
{
    public Guid Id { get; init; }
    public string? PasswordHash { get; init; }
    public bool PhoneVerified { get; init; }
    public bool IsActive { get; init; }
    public int FailedLoginCount { get; init; }
    public DateTime? LockedUntil { get; init; }
}

internal sealed class ParentTokenRow
{
    public string Phone { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

internal sealed class ParentProfileRow
{
    public string FullName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public bool PhoneVerified { get; init; }
    public bool HasPaymentPin { get; init; }
}

internal sealed class ParentRepository : BaseRepository, IParentRepository
{
    public ParentRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<bool> ExistsByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM parents WHERE phone = @Phone", new { Phone = phone }, cancellationToken);
        return count > 0;
    }

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM parents WHERE email = @Email", new { Email = email }, cancellationToken);
        return count > 0;
    }

    public Task<Guid> CreateAsync(string firstName, string? middleName, string lastName, string phone,
        string? email, string passwordHash, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO parents (first_name, middle_name, last_name, phone, email, password_hash)
            VALUES (@FirstName, @MiddleName, @LastName, @Phone, @Email, @PasswordHash)
            RETURNING id
            """,
            new { FirstName = firstName, MiddleName = middleName, LastName = lastName, Phone = phone, Email = email, PasswordHash = passwordHash },
            cancellationToken);
    }

    public Task<ParentClaimState?> GetClaimStateByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ParentClaimState?>(
            "SELECT id AS Id, (password_hash IS NOT NULL) AS HasPassword FROM parents WHERE phone = @Phone",
            new { Phone = phone }, cancellationToken);
    }

    public Task<int> ClaimAsync(Guid parentId, string firstName, string? middleName, string lastName,
        string? email, string passwordHash, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE parents
               SET first_name = @FirstName,
                   middle_name = @MiddleName,
                   last_name = @LastName,
                   email = COALESCE(@Email, email),
                   password_hash = @PasswordHash,
                   updated_at = NOW()
             WHERE id = @Id AND password_hash IS NULL
            """,
            new { Id = parentId, FirstName = firstName, MiddleName = middleName, LastName = lastName,
                Email = email, PasswordHash = passwordHash },
            cancellationToken);
    }

    public Task<Guid?> GetIdByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM parents WHERE phone = @Phone", new { Phone = phone }, cancellationToken);
    }

    public Task MarkPhoneVerifiedAsync(Guid parentId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE parents SET phone_verified = TRUE, status = 'active', updated_at = NOW() WHERE id = @Id",
            new { Id = parentId }, cancellationToken);
    }

    public Task<ParentLoginRow?> GetByPhoneForLoginAsync(string phone, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ParentLoginRow>(
            """
            SELECT id, password_hash, phone_verified, is_active, failed_login_count, locked_until
            FROM parents
            WHERE phone = @Phone
            """,
            new { Phone = phone }, cancellationToken);
    }

    public Task SetLoginFailureAsync(Guid parentId, int failedCount, DateTime? lockedUntil,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE parents
            SET failed_login_count = @FailedCount, locked_until = @LockedUntil, updated_at = NOW()
            WHERE id = @Id
            """,
            new { Id = parentId, FailedCount = failedCount, LockedUntil = lockedUntil }, cancellationToken);
    }

    public Task ClearLoginFailuresAsync(Guid parentId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE parents SET failed_login_count = 0, locked_until = NULL, updated_at = NOW() WHERE id = @Id",
            new { Id = parentId }, cancellationToken);
    }

    public Task<ParentTokenRow?> GetTokenClaimsAsync(Guid parentId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ParentTokenRow>(
            "SELECT phone, is_active FROM parents WHERE id = @Id", new { Id = parentId }, cancellationToken);
    }

    public Task SetPaymentPinAsync(Guid parentId, string pinHash, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE parents
            SET payment_pin_hash = @PinHash, pin_failed_count = 0, pin_locked_until = NULL, updated_at = NOW()
            WHERE id = @Id
            """,
            new { Id = parentId, PinHash = pinHash }, cancellationToken);
    }

    public Task SetPasswordAsync(Guid parentId, string passwordHash, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE parents SET password_hash = @PasswordHash, updated_at = NOW() WHERE id = @Id",
            new { Id = parentId, PasswordHash = passwordHash }, cancellationToken);
    }

    public Task<ParentProfileRow?> GetProfileAsync(Guid parentId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ParentProfileRow>(
            """
            SELECT concat_ws(' ', first_name, middle_name, last_name) AS full_name,
                   phone, email, phone_verified, (payment_pin_hash IS NOT NULL) AS has_payment_pin
            FROM parents
            WHERE id = @Id
            """,
            new { Id = parentId }, cancellationToken);
    }
}
