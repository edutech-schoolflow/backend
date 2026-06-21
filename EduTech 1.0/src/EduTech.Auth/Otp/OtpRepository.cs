using EduTech.Shared.Persistence;

namespace EduTech.Auth.Otp;

/// <summary>
/// <c>otp_codes</c> is a GLOBAL table (no tenant) — derives from <see cref="BaseRepository"/>,
/// not <see cref="TenantRepository"/>.
/// </summary>
internal sealed class OtpRepository : BaseRepository, IOtpRepository
{
    public OtpRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task InvalidateActiveAsync(string purpose, Guid targetId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE otp_codes SET used_at = NOW() " +
            "WHERE target_type = @Purpose AND target_id = @TargetId AND used_at IS NULL",
            new { Purpose = purpose, TargetId = targetId }, cancellationToken);
    }

    public Task InsertAsync(string purpose, Guid targetId, string phone, string codeHash,
        DateTime expiresAt, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "INSERT INTO otp_codes (target_type, target_id, phone, code_hash, expires_at) " +
            "VALUES (@Purpose, @TargetId, @Phone, @CodeHash, @ExpiresAt)",
            new
            {
                Purpose = purpose,
                TargetId = targetId,
                Phone = phone,
                CodeHash = codeHash,
                ExpiresAt = expiresAt
            }, cancellationToken);
    }

    public Task<OtpCodeRow?> GetLatestActiveAsync(string purpose, Guid targetId, CancellationToken cancellationToken)
    {
        return QueryFirstOrDefaultAsync<OtpCodeRow>(
            "SELECT id, code_hash, expires_at, attempts FROM otp_codes " +
            "WHERE target_type = @Purpose AND target_id = @TargetId AND used_at IS NULL " +
            "ORDER BY created_at DESC LIMIT 1",
            new { Purpose = purpose, TargetId = targetId }, cancellationToken);
    }

    public Task IncrementAttemptsAsync(Guid id, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE otp_codes SET attempts = attempts + 1 WHERE id = @Id",
            new { Id = id }, cancellationToken);
    }

    public Task MarkUsedAsync(Guid id, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE otp_codes SET used_at = NOW() WHERE id = @Id",
            new { Id = id }, cancellationToken);
    }
}
