namespace EduTech.Auth.SchoolOwner;

/// <summary>
/// The access + refresh token pair produced by login/refresh. The controller decides how to return
/// it (httpOnly cookies per Cross-Cutting Auth §X.2, and/or body).
/// </summary>
public sealed class LoginResult
{
    public required string AccessToken { get; init; }
    public required DateTime AccessTokenExpiresAt { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime RefreshTokenExpiresAt { get; init; }
}
