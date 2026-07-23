namespace EduTech.Auth.RefreshTokens;

/// <summary>Data access for the global <c>refresh_tokens</c> table (no school_id).</summary>
internal interface IRefreshTokenRepository
{
    Task InsertAsync(string actorType, Guid actorId, Guid? identityId, Guid? contextId, string tokenHash,
        Guid familyId, DateTime expiresAt, string? ipAddress, string? userAgent, CancellationToken cancellationToken);

    Task<RefreshTokenRow?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task MarkRotatedAsync(Guid id, CancellationToken cancellationToken);

    Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken);

    Task RevokeAllForActorAsync(string actorType, Guid actorId, CancellationToken cancellationToken);
}
