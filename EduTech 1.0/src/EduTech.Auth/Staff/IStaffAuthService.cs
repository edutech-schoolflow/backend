namespace EduTech.Auth.Staff;

/// <summary>
/// Standalone staff auth flows (Path A): register → verify phone → login → refresh. Login issues an
/// identity-only token (no school). Invite acceptance + affiliation-scoped tokens come later.
/// Public because the module's controller depends on it; the implementation stays internal.
/// </summary>
public interface IStaffAuthService
{
    Task RegisterAsync(RegisterStaffRequest request, CancellationToken cancellationToken);

    Task VerifyPhoneAsync(StaffVerifyPhoneRequest request, CancellationToken cancellationToken);

    Task<StaffTokensResult> LoginAsync(StaffLoginRequest request, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken);

    Task<StaffTokensResult> RefreshAsync(string refreshToken, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken);

    Task ResendOtpAsync(StaffResendOtpRequest request, CancellationToken cancellationToken);

    Task ForgotPasswordAsync(StaffForgotPasswordRequest request, CancellationToken cancellationToken);

    Task ResetPasswordAsync(StaffResetPasswordRequest request, CancellationToken cancellationToken);

    Task<StaffMeResponse> GetMeAsync(CancellationToken cancellationToken);
}
