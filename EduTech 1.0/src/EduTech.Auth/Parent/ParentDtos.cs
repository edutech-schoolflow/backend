namespace EduTech.Auth.Parent;

/// <summary>Parent self-registration — phone-first (email optional).</summary>
public sealed class RegisterParentRequest
{
    public string FullName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? Email { get; init; }
}

public sealed class ParentVerifyPhoneRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

public sealed class ParentLoginRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>Set/replace the 6-digit payment PIN (separate from the login password).</summary>
public sealed class SetPaymentPinRequest
{
    public string Pin { get; init; } = string.Empty;
}

/// <summary>Access + refresh pair from parent login/refresh (controller sets cookies).</summary>
public sealed class ParentTokensResult
{
    public required string AccessToken { get; init; }
    public required DateTime AccessTokenExpiresAt { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime RefreshTokenExpiresAt { get; init; }
}

/// <summary>Login/refresh response body — tokens go to cookies; this carries expiry only.</summary>
public sealed class ParentAuthResponse
{
    public required DateTime AccessTokenExpiresAt { get; init; }
}

public sealed class ParentResendOtpRequest
{
    public string Phone { get; init; } = string.Empty;
}

public sealed class ParentForgotPasswordRequest
{
    public string Phone { get; init; } = string.Empty;
}

public sealed class ParentResetPasswordRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>GET /me — parent profile.</summary>
public sealed class ParentMeResponse
{
    public required string FullName { get; init; }
    public required string Phone { get; init; }
    public string? Email { get; init; }
    public required bool PhoneVerified { get; init; }
    public required bool HasPaymentPin { get; init; }
}
