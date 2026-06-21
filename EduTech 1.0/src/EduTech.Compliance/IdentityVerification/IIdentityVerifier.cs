namespace EduTech.Compliance.IdentityVerification;

/// <summary>
/// Verifies a Nigerian NIN with an identity provider (Dojah in production; a stub auto-verifies in
/// dev so the flow runs without provider credentials). Liveness/selfie is gated client-side and not
/// part of this contract yet.
/// </summary>
public interface IIdentityVerifier
{
    Task<NinVerificationResult> VerifyNinAsync(string nin, CancellationToken cancellationToken = default);
}

public sealed class NinVerificationResult
{
    public required bool Verified { get; init; }
    public string? Reason { get; init; }

    public static NinVerificationResult Ok() => new NinVerificationResult { Verified = true };
    public static NinVerificationResult Fail(string reason) => new NinVerificationResult { Verified = false, Reason = reason };
}
