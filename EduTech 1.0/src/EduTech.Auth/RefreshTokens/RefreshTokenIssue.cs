namespace EduTech.Auth.RefreshTokens;

/// <summary>
/// A newly issued refresh token. <see cref="Token"/> is the RAW value to set in an httpOnly cookie;
/// only its hash is stored.
/// </summary>
public sealed class RefreshTokenIssue
{
    public required string Token { get; init; }
    public required Guid FamilyId { get; init; }
    public required DateTime ExpiresAt { get; init; }
}
