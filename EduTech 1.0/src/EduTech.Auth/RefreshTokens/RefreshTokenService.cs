using System.Security.Cryptography;
using System.Text;

namespace EduTech.Auth.RefreshTokens;

/// <summary>
/// Refresh-token service (see <see cref="IRefreshTokenService"/>). Tokens are 256-bit random values
/// (hex), stored as a SHA-256 hash (high entropy ⇒ a fast hash is sufficient, unlike the OTP code).
/// Rotation is one-time-use with theft detection: presenting an already-rotated/revoked token nukes
/// the family.
/// </summary>
internal sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly IRefreshTokenRepository _repository;

    public RefreshTokenService(IRefreshTokenRepository repository)
    {
        _repository = repository;
    }

    public async Task<RefreshTokenIssue> IssueAsync(string actorType, Guid actorId, Guid? identityId,
        Guid? contextId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        string token = GenerateToken();
        Guid familyId = Guid.NewGuid();
        DateTime expiresAt = DateTime.UtcNow.Add(LifetimeFor(actorType));

        await _repository.InsertAsync(actorType, actorId, identityId, contextId, HashToken(token), familyId,
            expiresAt, ipAddress, userAgent, cancellationToken);

        return new RefreshTokenIssue { Token = token, FamilyId = familyId, ExpiresAt = expiresAt };
    }

    public async Task<RefreshRotationResult> RotateAsync(string rawToken, string? ipAddress,
        string? userAgent, CancellationToken cancellationToken = default)
    {
        RefreshTokenRow? row = await _repository.GetByHashAsync(HashToken(rawToken), cancellationToken);
        if (row is null)
        {
            return RefreshRotationResult.Fail(RefreshTokenStatus.NotFound);
        }

        // Reuse of an already-rotated or revoked token ⇒ assume theft ⇒ revoke the whole family.
        if (row.RotatedAt is not null || row.RevokedAt is not null)
        {
            await _repository.RevokeFamilyAsync(row.FamilyId, cancellationToken);
            return RefreshRotationResult.Fail(RefreshTokenStatus.Reused);
        }

        if (row.ExpiresAt < DateTime.UtcNow)
        {
            return RefreshRotationResult.Fail(RefreshTokenStatus.Expired);
        }

        await _repository.MarkRotatedAsync(row.Id, cancellationToken);

        string newToken = GenerateToken();
        // Same family, same expiry, same key (actor + canonical identity/context) — rotation continues
        // the lineage, it does not extend the session or change who it belongs to.
        await _repository.InsertAsync(row.ActorType, row.ActorId, row.IdentityId, row.ContextId,
            HashToken(newToken), row.FamilyId, row.ExpiresAt, ipAddress, userAgent, cancellationToken);

        return new RefreshRotationResult
        {
            Status = RefreshTokenStatus.Success,
            NewToken = newToken,
            ActorType = row.ActorType,
            ActorId = row.ActorId,
            IdentityId = row.IdentityId,
            ContextId = row.ContextId,
            ExpiresAt = row.ExpiresAt
        };
    }

    public Task RevokeAllForActorAsync(string actorType, Guid actorId, CancellationToken cancellationToken = default)
    {
        return _repository.RevokeAllForActorAsync(actorType, actorId, cancellationToken);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        RefreshTokenRow? row = await _repository.GetByHashAsync(HashToken(rawToken), cancellationToken);
        if (row is not null)
        {
            await _repository.RevokeFamilyAsync(row.FamilyId, cancellationToken);
        }
    }

    private static TimeSpan LifetimeFor(string actorType)
    {
        return actorType switch
        {
            AuthActorTypes.Parent => TimeSpan.FromDays(14),
            AuthActorTypes.PlatformAdmin => TimeSpan.FromHours(8),
            _ => TimeSpan.FromHours(12)   // school_owner, staff
        };
    }

    private static string GenerateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private static string HashToken(string token)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
