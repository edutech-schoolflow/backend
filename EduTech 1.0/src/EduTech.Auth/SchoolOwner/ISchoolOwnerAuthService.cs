namespace EduTech.Auth.SchoolOwner;

/// <summary>
/// Auth flows for the School Owner (Actor 1): register, verify phone, login, refresh.
/// Public because the module's controller (a public type) depends on it; the implementation stays
/// internal.
/// </summary>
public interface ISchoolOwnerAuthService
{
    /// <summary>
    /// Atomically creates the school shell + owner account, then sends a phone-verification OTP.
    /// The owner must verify their phone before they can log in.
    /// </summary>
    Task RegisterAsync(RegisterSchoolOwnerRequest request, CancellationToken cancellationToken);

    /// <summary>Verifies the registration OTP and marks the owner's phone as verified.</summary>
    Task VerifyPhoneAsync(VerifyPhoneRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Authenticates phone + password and issues an access + refresh token pair. Applies failed-login
    /// lockout. <paramref name="ipAddress"/>/<paramref name="userAgent"/> are recorded on the refresh
    /// token (supplied by the controller from the request).
    /// </summary>
    Task<LoginResult> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rotates a refresh token and mints a fresh access token, re-reading the owner's current
    /// school/kyc status so status changes (KYC approval, deactivation) propagate.
    /// </summary>
    Task<LoginResult> RefreshAsync(string refreshToken, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken);

    /// <summary>Resends the registration OTP (no-op + uniform response if already verified/unknown).</summary>
    Task ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken);

    /// <summary>Sends a password-reset OTP (always reports success — no account enumeration).</summary>
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>Verifies the reset OTP, sets the new password, and revokes all sessions.</summary>
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>Profile of the currently authenticated owner.</summary>
    Task<SchoolOwnerMeResponse> GetMeAsync(CancellationToken cancellationToken);
}
