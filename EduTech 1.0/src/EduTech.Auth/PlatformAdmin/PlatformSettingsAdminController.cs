using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Auth.PlatformAdmin;

/// <summary>Platform-wide settings (Platform Admin). The payment fee is editable by finance/super.</summary>
[ApiController]
[Route("api/v1/admin/settings")]
[Authorize(Policy = "PlatformAdminOnly")]
public sealed class PlatformSettingsAdminController : ControllerBase
{
    private readonly IPlatformSettingsAdminService _service;

    public PlatformSettingsAdminController(IPlatformSettingsAdminService service)
    {
        _service = service;
    }

    /// <summary>The flat per-transaction payment fee (naira), added on top of every payment.</summary>
    [HttpGet("payment-fee")]
    public async Task<ActionResult<ServiceResponses<object>>> GetPaymentFee(CancellationToken cancellationToken)
    {
        decimal amount = await _service.GetPaymentFeeAsync(cancellationToken);
        return Ok(ServiceResponses<object>.Ok(new { amount }, "Payment fee."));
    }

    [HttpPut("payment-fee")]
    public async Task<ActionResult<ServiceResponses<object>>> SetPaymentFee(
        [FromBody] SetPaymentFeeRequest request, CancellationToken cancellationToken)
    {
        await _service.SetPaymentFeeAsync(request.Amount, cancellationToken);
        return Ok(ServiceResponses<object>.Ok(new { amount = request.Amount }, "Payment fee updated."));
    }
}

public sealed class SetPaymentFeeRequest
{
    public decimal Amount { get; init; }
}
