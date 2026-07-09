using EduTech.Identity.Domain;
using EduTech.Shared.Persistence;

namespace EduTech.Identity;

/// <summary>
/// Data access for the global <c>identities</c> table. Loads rows and rehydrates the
/// <see cref="Domain.Identity"/> aggregate; writes are guarded where the aggregate's invariants
/// demand it (claim). Not tenant-scoped — identities are school-agnostic by definition.
/// </summary>
internal interface IIdentityRepository
{
    Task<Domain.Identity?> GetByPhoneAsync(string phone, CancellationToken cancellationToken);
    Task<Domain.Identity?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Fresh registration: creates a claimed (password-set) but unverified identity.</summary>
    Task<Guid> CreateAsync(string firstName, string? middleName, string lastName, string phone,
        string? email, string passwordHash, CancellationToken cancellationToken);

    /// <summary>
    /// Pre-creates an unclaimed identity (no password) — e.g. a school admitting a student against a
    /// parent's phone. Returns the existing id if the phone is already known (idempotent).
    /// </summary>
    Task<Guid> EnsurePendingAsync(string firstName, string lastName, string phone, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a claim. Guarded by <c>password_hash IS NULL</c>: returns rows affected —
    /// 0 means a concurrent claim won and the caller must 409.
    /// </summary>
    Task<int> ClaimAsync(Guid identityId, string firstName, string? middleName, string lastName,
        string? email, string passwordHash, CancellationToken cancellationToken);

    Task MarkPhoneVerifiedAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>Sets a new password and clears any lockout (used by reset-password).</summary>
    Task SetPasswordAsync(Guid identityId, string passwordHash, CancellationToken cancellationToken);
    Task SaveLoginStateAsync(Guid identityId, int failedLoginCount, DateTime? lockedUntil,
        bool successful, CancellationToken cancellationToken);
}

internal sealed class IdentityRow
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PasswordHash { get; init; }
    public bool PhoneVerified { get; init; }
    public bool EmailVerified { get; init; }
    public string Status { get; init; } = "pending";
    public int FailedLoginCount { get; init; }
    public DateTime? LockedUntil { get; init; }
}

internal sealed class IdentityRepository : BaseRepository, IIdentityRepository
{
    private const string Columns =
        "id, first_name AS FirstName, middle_name AS MiddleName, last_name AS LastName, phone, " +
        "email, password_hash AS PasswordHash, phone_verified AS PhoneVerified, " +
        "email_verified AS EmailVerified, status, failed_login_count AS FailedLoginCount, " +
        "locked_until AS LockedUntil";

    public IdentityRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<Domain.Identity?> GetByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        IdentityRow? row = await QuerySingleOrDefaultAsync<IdentityRow>(
            $"SELECT {Columns} FROM identities WHERE phone = @Phone", new { Phone = phone }, cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public async Task<Domain.Identity?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        IdentityRow? row = await QuerySingleOrDefaultAsync<IdentityRow>(
            $"SELECT {Columns} FROM identities WHERE id = @Id", new { Id = id }, cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public Task<Guid> CreateAsync(string firstName, string? middleName, string lastName, string phone,
        string? email, string passwordHash, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO identities (first_name, middle_name, last_name, phone, email, password_hash, status)
            VALUES (@FirstName, @MiddleName, @LastName, @Phone, @Email, @PasswordHash, 'pending')
            RETURNING id
            """,
            new { FirstName = firstName, MiddleName = middleName, LastName = lastName, Phone = phone,
                  Email = email, PasswordHash = passwordHash },
            cancellationToken);
    }

    public async Task<Guid> EnsurePendingAsync(string firstName, string lastName, string phone,
        CancellationToken cancellationToken)
    {
        Guid? existing = await QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM identities WHERE phone = @Phone", new { Phone = phone }, cancellationToken);
        if (existing is Guid id)
        {
            return id;
        }

        return await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO identities (first_name, last_name, phone, status)
            VALUES (@FirstName, @LastName, @Phone, 'pending')
            ON CONFLICT (phone) DO UPDATE SET updated_at = NOW()
            RETURNING id
            """,
            new { FirstName = firstName, LastName = lastName, Phone = phone }, cancellationToken);
    }

    public Task<int> ClaimAsync(Guid identityId, string firstName, string? middleName, string lastName,
        string? email, string passwordHash, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE identities
               SET first_name = @FirstName,
                   middle_name = @MiddleName,
                   last_name = @LastName,
                   email = COALESCE(@Email, email),
                   password_hash = @PasswordHash,
                   updated_at = NOW()
             WHERE id = @Id AND password_hash IS NULL
            """,
            new { Id = identityId, FirstName = firstName, MiddleName = middleName, LastName = lastName,
                  Email = email, PasswordHash = passwordHash },
            cancellationToken);
    }

    public Task MarkPhoneVerifiedAsync(Guid identityId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE identities
               SET phone_verified = TRUE,
                   status = CASE WHEN password_hash IS NOT NULL AND status = 'pending' THEN 'active' ELSE status END,
                   updated_at = NOW()
             WHERE id = @Id
            """,
            new { Id = identityId }, cancellationToken);
    }

    public Task SetPasswordAsync(Guid identityId, string passwordHash, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE identities
               SET password_hash = @Hash, failed_login_count = 0, locked_until = NULL, updated_at = NOW()
             WHERE id = @Id
            """,
            new { Id = identityId, Hash = passwordHash }, cancellationToken);
    }

    public Task SaveLoginStateAsync(Guid identityId, int failedLoginCount, DateTime? lockedUntil,
        bool successful, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE identities
               SET failed_login_count = @Failed, locked_until = @LockedUntil,
                   last_login_at = CASE WHEN @Successful THEN NOW() ELSE last_login_at END,
                   updated_at = NOW()
             WHERE id = @Id
            """,
            new { Id = identityId, Failed = failedLoginCount, LockedUntil = lockedUntil, Successful = successful },
            cancellationToken);
    }

    private static Domain.Identity Rehydrate(IdentityRow r) => new Domain.Identity(
        r.Id, r.FirstName, r.MiddleName, r.LastName, r.Phone, r.Email, r.PasswordHash,
        r.PhoneVerified, r.EmailVerified,
        r.Status switch { "active" => IdentityStatus.Active, "suspended" => IdentityStatus.Suspended, _ => IdentityStatus.Pending },
        r.FailedLoginCount, r.LockedUntil);
}
