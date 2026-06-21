namespace EduTech.Auth.RefreshTokens;

/// <summary>Outcome of rotating a refresh token.</summary>
public enum RefreshTokenStatus
{
    Success,
    NotFound,
    Expired,

    /// <summary>
    /// The presented token was already rotated or revoked — treated as theft. The whole token
    /// family is revoked and the user must log in again.
    /// </summary>
    Reused
}
