namespace EduTech.Auth.RefreshTokens;

/// <summary>
/// Issues, rotates, and revokes refresh tokens (Cross-Cutting Auth §X.1). Tokens are opaque random
/// values (NOT JWTs), stored hashed, rotated on every use, with reuse treated as theft.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Issues a new refresh token for an actor, starting a new family. Lifetime is per-portal
    /// (parent 14d, platform admin 8h, school owner/staff 12h). Returns the RAW token.
    /// </summary>
    Task<RefreshTokenIssue> IssueAsync(string actorType, Guid actorId, string? ipAddress,
        string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and rotates a presented refresh token: marks it rotated and issues a replacement in
    /// the same family (same expiry — families do not extend forever). Reuse of an already-rotated or
    /// revoked token revokes the whole family.
    /// </summary>
    Task<RefreshRotationResult> RotateAsync(string rawToken, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every active refresh token for an actor — on password reset, deactivation, suspension.
    /// </summary>
    Task RevokeAllForActorAsync(string actorType, Guid actorId, CancellationToken cancellationToken = default);

    /// <summary>Revokes a single session by its refresh token (and its rotation family) — for logout.</summary>
    Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default);
}
