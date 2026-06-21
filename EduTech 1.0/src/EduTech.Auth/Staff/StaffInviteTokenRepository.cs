using System.Data;
using EduTech.Shared.Persistence;

namespace EduTech.Auth.Staff;

/// <summary>
/// Data access for <c>staff_invite_tokens</c> — ties an SMS invite link to a pending affiliation.
/// Tokens are opaque random values stored as a SHA-256 hash.
/// </summary>
internal interface IStaffInviteTokenRepository
{
    Task CreateAsync(Guid affiliationId, string phone, string tokenHash, DateTime expiresAt,
        IDbTransaction transaction, CancellationToken cancellationToken);

    Task<StaffInviteTokenRow?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task MarkUsedAsync(Guid id, IDbTransaction transaction, CancellationToken cancellationToken);
}

internal sealed class StaffInviteTokenRow
{
    public Guid Id { get; init; }
    public Guid AffiliationId { get; init; }
    public string Phone { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public DateTime? UsedAt { get; init; }
}

internal sealed class StaffInviteTokenRepository : BaseRepository, IStaffInviteTokenRepository
{
    public StaffInviteTokenRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task CreateAsync(Guid affiliationId, string phone, string tokenHash, DateTime expiresAt,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO staff_invite_tokens (affiliation_id, phone, token_hash, expires_at)
            VALUES (@AffiliationId, @Phone, @TokenHash, @ExpiresAt)
            """,
            new
            {
                AffiliationId = affiliationId,
                Phone = phone,
                TokenHash = tokenHash,
                ExpiresAt = expiresAt
            },
            cancellationToken, transaction);
    }

    public Task<StaffInviteTokenRow?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StaffInviteTokenRow>(
            """
            SELECT id, affiliation_id, phone, expires_at, used_at
            FROM staff_invite_tokens
            WHERE token_hash = @TokenHash
            """,
            new { TokenHash = tokenHash }, cancellationToken);
    }

    public Task MarkUsedAsync(Guid id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE staff_invite_tokens SET used_at = NOW() WHERE id = @Id",
            new { Id = id }, cancellationToken, transaction);
    }
}
