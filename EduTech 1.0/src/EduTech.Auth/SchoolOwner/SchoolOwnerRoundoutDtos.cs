namespace EduTech.Auth.SchoolOwner;

/// <summary>Resend the registration OTP to a phone.</summary>
public sealed class ResendOtpRequest
{
    public string Phone { get; init; } = string.Empty;
}

/// <summary>Start a password reset — sends an OTP to the phone.</summary>
public sealed class ForgotPasswordRequest
{
    public string Phone { get; init; } = string.Empty;
}

/// <summary>Complete a password reset with the OTP + a new password.</summary>
public sealed class ResetPasswordRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>GET /me response for the school owner.</summary>
public sealed class SchoolOwnerMeResponse
{
    public required string FullName { get; init; }
    public required string Phone { get; init; }
    public string? Email { get; init; }
    public required bool PhoneVerified { get; init; }
    public required Guid SchoolId { get; init; }
    public required string SchoolStatus { get; init; }
    public required string KycStatus { get; init; }
    public string? Subdomain { get; init; }
}
