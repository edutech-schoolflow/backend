using Microsoft.Extensions.Logging;

namespace EduTech.Compliance.IdentityVerification;

/// <summary>
/// Dev fallback when Dojah isn't configured: any 11-digit NIN auto-verifies, so the compliance flow
/// runs end-to-end without provider credentials. NOT for production.
/// </summary>
public sealed class StubNinVerifier : IIdentityVerifier
{
    private readonly ILogger<StubNinVerifier> _logger;

    public StubNinVerifier(ILogger<StubNinVerifier> logger)
    {
        _logger = logger;
    }

    public Task<NinVerificationResult> VerifyNinAsync(string nin, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[STUB] NIN verification auto-approved (Dojah not configured).");
        return Task.FromResult(NinVerificationResult.Ok());
    }
}
