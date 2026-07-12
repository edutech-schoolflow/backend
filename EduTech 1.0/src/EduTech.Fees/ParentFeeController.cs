using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Fees;

/// <summary>Parent-side fees + payments (parent-pull). Ownership-scoped to the parent's own children.</summary>
[ApiController]
[Authorize(Policy = "AuthenticatedIdentity")]
public sealed class ParentFeeController : ControllerBase
{
    private readonly IParentFeeService _service;

    public ParentFeeController(IParentFeeService service)
    {
        _service = service;
    }

    /// <summary>Per child, the approved fees applicable to them (compulsory + optional, tagged).</summary>
    [HttpGet("api/v1/family/fees")]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ChildFeesResponse>>>> Fees(
        [FromQuery] Guid? studentId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChildFeesResponse> fees = await _service.GetFeesAsync(studentId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ChildFeesResponse>>.Ok(fees, "Fees."));
    }

    /// <summary>Pay (part of) a fee for a child — PIN-authorized. Paying an optional fee subscribes the child.</summary>
    [HttpPost("api/v1/family/fees/pay")]
    public async Task<ActionResult<ServiceResponses<PaymentResponse>>> Pay(
        [FromBody] PayFeeRequest request, CancellationToken cancellationToken)
    {
        PaymentResponse payment = await _service.PayAsync(request, cancellationToken);
        return Ok(ServiceResponses<PaymentResponse>.Ok(payment, "Payment successful."));
    }

}
