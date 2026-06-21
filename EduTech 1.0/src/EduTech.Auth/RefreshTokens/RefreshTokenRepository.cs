using EduTech.Shared.Persistence;

namespace EduTech.Auth.RefreshTokens;

/// <summary><c>refresh_tokens</c> is a GLOBAL table — derives from <see cref="BaseRepository"/>.</summary>
internal sealed class RefreshTokenRepository : BaseRepository, IRefreshTokenRepository
{
    public RefreshTokenRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task InsertAsync(string actorType, Guid actorId, string tokenHash, Guid familyId,
        DateTime expiresAt, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "INSERT INTO refresh_tokens " +
            "(actor_type, actor_id, token_hash, family_id, expires_at, ip_address, user_agent) " +
            "VALUES (@ActorType, @ActorId, @TokenHash, @FamilyId, @ExpiresAt, @IpAddress, @UserAgent)",
            new
            {
                ActorType = actorType,
                ActorId = actorId,
                TokenHash = tokenHash,
                FamilyId = familyId,
                ExpiresAt = expiresAt,
                IpAddress = ipAddress,
                UserAgent = userAgent
            }, cancellationToken);
    }

    public Task<RefreshTokenRow?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        return QueryFirstOrDefaultAsync<RefreshTokenRow>(
            "SELECT id, actor_type, actor_id, family_id, expires_at, rotated_at, revoked_at " +
            "FROM refresh_tokens WHERE token_hash = @TokenHash",
            new { TokenHash = tokenHash }, cancellationToken);
    }

    public Task MarkRotatedAsync(Guid id, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE refresh_tokens SET rotated_at = NOW() WHERE id = @Id",
            new { Id = id }, cancellationToken);
    }

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = NOW() " +
            "WHERE family_id = @FamilyId AND revoked_at IS NULL",
            new { FamilyId = familyId }, cancellationToken);
    }

    public Task RevokeAllForActorAsync(string actorType, Guid actorId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = NOW() " +
            "WHERE actor_type = @ActorType AND actor_id = @ActorId AND revoked_at IS NULL",
            new { ActorType = actorType, ActorId = actorId }, cancellationToken);
    }
}
