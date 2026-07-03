namespace EduTech.Shared.Identity;

/// <summary>
/// Verifies a Nigerian NIN/BVN with an identity provider (Dojah in production; a stub auto-verifies
/// in dev so flows run without provider credentials). Shared by staff/parent compliance and school
/// proprietor KYC. Liveness/selfie is gated client-side and not part of this contract.
/// </summary>
public interface IIdentityVerifier
{
    /// <summary>Verifies the NIN exists AND its registered name matches <paramref name="expectedName"/>.</summary>
    Task<IdentityVerificationResult> VerifyNinAsync(string nin, string expectedName,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies the BVN exists AND its registered name matches <paramref name="expectedName"/>.</summary>
    Task<IdentityVerificationResult> VerifyBvnAsync(string bvn, string expectedName,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityVerificationResult
{
    public required bool Verified { get; init; }
    public string? Reason { get; init; }

    public static IdentityVerificationResult Ok() => new IdentityVerificationResult { Verified = true };
    public static IdentityVerificationResult Fail(string reason) =>
        new IdentityVerificationResult { Verified = false, Reason = reason };
}
