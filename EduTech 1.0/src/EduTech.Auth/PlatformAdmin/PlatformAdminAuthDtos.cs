namespace EduTech.Auth.PlatformAdmin;

/// <summary>Dev-only seed of the first super_admin (only works while no admins exist).</summary>
public sealed class SeedAdminRequest
{
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>Platform admin login — email + password (TOTP is a planned follow-up).</summary>
public sealed class AdminLoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>Access + refresh pair from admin login (controller sets cookies).</summary>
public sealed class AdminTokensResult
{
    public required string AccessToken { get; init; }
    public required DateTime AccessTokenExpiresAt { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime RefreshTokenExpiresAt { get; init; }
}

/// <summary>Login response body — tokens go to cookies; this carries expiry only.</summary>
public sealed class AdminAuthResponse
{
    public required DateTime AccessTokenExpiresAt { get; init; }
}
