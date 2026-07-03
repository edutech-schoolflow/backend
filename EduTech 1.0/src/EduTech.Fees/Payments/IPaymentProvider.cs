namespace EduTech.Fees.Payments;

public sealed class ChargeRequest
{
    public required Guid ParentId { get; init; }
    public required Guid SchoolId { get; init; }
    public required decimal TotalCharged { get; init; }   // base + platform_fee
    public required string Reference { get; init; }        // our payment id, for idempotency
}

public sealed class ChargeResult
{
    public required string ProviderReference { get; init; }
    public required string Method { get; init; }           // virtual_account|card|...|stub
    /// <summary>True when the charge settled synchronously (dev stub). Real Monnify returns false
    /// and the payment stays pending until its signed webhook confirms.</summary>
    public required bool Succeeded { get; init; }
}

/// <summary>
/// Payment rail seam (Strategy, like ISmsProvider / IFileStorage). The dev stub auto-confirms; a real
/// MonnifyPaymentProvider plugs in via config later. The service flow (create pending -> confirm ->
/// allocate) is identical either way.
/// </summary>
public interface IPaymentProvider
{
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken);
}
