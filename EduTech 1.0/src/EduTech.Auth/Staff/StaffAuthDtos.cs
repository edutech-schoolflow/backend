namespace EduTech.Auth.Staff;

/// <summary>Staff phone-verification payload: phone + the 6-digit OTP.</summary>
public sealed class StaffVerifyPhoneRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

/// <summary>Staff login payload — phone + password.</summary>
public sealed class StaffLoginRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>Access + refresh token pair from staff login/refresh (controller sets cookies).</summary>
public sealed class StaffTokensResult
{
    public required string AccessToken { get; init; }
    public required DateTime AccessTokenExpiresAt { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime RefreshTokenExpiresAt { get; init; }
}

/// <summary>Login/refresh response body — tokens go to httpOnly cookies; this carries expiry only.</summary>
public sealed class StaffAuthResponse
{
    public required DateTime AccessTokenExpiresAt { get; init; }
}

public sealed class StaffResendOtpRequest
{
    public string Phone { get; init; } = string.Empty;
}

public sealed class StaffForgotPasswordRequest
{
    public string Phone { get; init; } = string.Empty;
}

public sealed class StaffResetPasswordRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>GET /me — staff identity profile (school context is separate, via /staff/schools).</summary>
public sealed class StaffMeResponse
{
    public required string FullName { get; init; }
    public required string Phone { get; init; }
    public string? Email { get; init; }
    public required bool PhoneVerified { get; init; }
    public required string KycStatus { get; init; }
}
