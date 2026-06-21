namespace EduTech.Auth.Tokens;

/// <summary>A minted JWT access token and the moment it expires.</summary>
internal sealed class AccessToken
{
    public required string Token { get; init; }
    public required DateTime ExpiresAt { get; init; }
}
