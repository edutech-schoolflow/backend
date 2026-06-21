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
    public DateTime ExpiresAt { get; init; }

    public bool IsSuccess => Status == RefreshTokenStatus.Success;

    public static RefreshRotationResult Fail(RefreshTokenStatus status) =>
        new RefreshRotationResult { Status = status };
}
