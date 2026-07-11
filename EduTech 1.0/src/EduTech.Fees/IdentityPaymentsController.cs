using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Fees;

/// <summary>
/// Identity-space "my payments" (EDD-002): the signed-in identity's payment history across schools,
/// authorized by the identity session. Includes application fees paid before any membership, which is
/// why payments live at the identity level rather than inside a single school's workspace.
/// </summary>
[ApiController]
[Route("api/v1/identity/payments")]
[Authorize(Policy = "AuthenticatedIdentity")]
public sealed class IdentityPaymentsController : ControllerBase
{
    private readonly IParentFeeService _service;

    public IdentityPaymentsController(IParentFeeService service)
    {
        _service = service;
    }

    /// <summary>My payment history, most recent first. Empty until I've paid anything.</summary>
    [HttpGet]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<PaymentResponse>>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PaymentResponse> payments = await _service.ListMyPaymentsAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<PaymentResponse>>.Ok(payments, "Payments."));
    }
}
