namespace EduTech.Auth.RefreshTokens;

/// <summary>
/// Result of rotating a refresh token. On success, carries the new raw token plus the actor it
/// belongs to, so the caller can mint a fresh access token.
/// </summary>
public sealed class RefreshRotationResult
{
    public required RefreshTokenStatus Status { get; init; }
    public string? NewToken { get; init; }
    public string? ActorType { get; init; }
    public Guid ActorId { get; init; }
    // Canonical key (EDD-012 B2c.3c): when present, refresh re-enters this context via the shared mint
    // instead of the legacy actor path.
    public Guid? IdentityId { get; init; }
    public Guid? ContextId { get; init; }
    public DateTime ExpiresAt { get; init; }

    public bool IsSuccess => Status == RefreshTokenStatus.Success;

    public static RefreshRotationResult Fail(RefreshTokenStatus status) =>
        new RefreshRotationResult { Status = status };
}
