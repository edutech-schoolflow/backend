using Microsoft.Extensions.Logging;

namespace EduTech.Fees.Payments;

/// <summary>
/// Dev payment provider: charges "succeed" immediately (no real money, no webhook), so the pay flow
/// is exercisable end-to-end. Swap for a real Monnify provider via config when credentials exist —
/// it would return <c>Succeeded = false</c> and the signed webhook would confirm.
/// </summary>
internal sealed class StubPaymentProvider : IPaymentProvider
{
    private readonly ILogger<StubPaymentProvider> _logger;

    public StubPaymentProvider(ILogger<StubPaymentProvider> logger)
    {
        _logger = logger;
    }

    public Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        string reference = $"STUB-{Guid.NewGuid():N}";
        _logger.LogInformation("[DEV PAYMENT] parent {Parent} charged {Amount} (ref {Ref}) — auto-confirmed.",
            request.ParentId, request.TotalCharged, reference);

        return Task.FromResult(new ChargeResult
        {
            ProviderReference = reference,
            Method = "stub",
            Succeeded = true
        });
    }
}
