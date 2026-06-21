namespace EduTech.Auth.Otp;

/// <summary>
/// The platform's single OTP generator/verifier. ONE component issues and checks every phone OTP —
/// school owner, staff, parent — distinguished by <see cref="OtpPurpose"/> + target id.
/// (Platform Admin uses TOTP, not phone OTP.)
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Generates a fresh 6-digit OTP for (purpose, targetId), invalidating any prior unused code for
    /// that pair, stores it hashed with a 5-minute expiry, and returns the RAW code so the caller can
    /// deliver it (SMS/WhatsApp). <paramref name="phone"/> is recorded as the recipient.
    /// </summary>
    Task<string> GenerateAsync(string purpose, Guid targetId, string phone,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a submitted code for (purpose, targetId). Enforces the 5-minute expiry and a maximum
    /// of 3 attempts; consumes the code on success.
    /// </summary>
    Task<OtpVerifyResult> VerifyAsync(string purpose, Guid targetId, string code,
        CancellationToken cancellationToken = default);
}
