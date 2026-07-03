using Microsoft.Extensions.Logging;

namespace EduTech.Shared.Identity;

/// <summary>Dev fallback when Dojah isn't configured: NIN/BVN auto-verify so flows run end-to-end. NOT for production.</summary>
public sealed class StubIdentityVerifier : IIdentityVerifier
{
    private readonly ILogger<StubIdentityVerifier> _logger;

    public StubIdentityVerifier(ILogger<StubIdentityVerifier> logger)
    {
        _logger = logger;
    }

    public Task<IdentityVerificationResult> VerifyNinAsync(string nin, string expectedName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[STUB] NIN verification auto-approved (Dojah not configured).");
        return Task.FromResult(IdentityVerificationResult.Ok());
    }

    public Task<IdentityVerificationResult> VerifyBvnAsync(string bvn, string expectedName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[STUB] BVN verification auto-approved (Dojah not configured).");
        return Task.FromResult(IdentityVerificationResult.Ok());
    }
}
