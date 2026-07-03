using EduTech.Shared.Auth;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Fees;

/// <summary>School-side fees (SchoolPortal). Setup + invoicing need manage_fees; reads need view_invoices.</summary>
[ApiController]
[Route("api/v1/school")]
[Authorize(Policy = "SchoolPortal")]
public sealed class SchoolFeeController : ControllerBase
{
    private readonly ISchoolFeeService _service;

    public SchoolFeeController(ISchoolFeeService service)
    {
        _service = service;
    }

    [HttpPost("fee-types")]
    [RequireFeature(StaffFeatureFlags.ManageFees)]
    public async Task<ActionResult<ServiceResponses<FeeTypeResponse>>> CreateFeeType(
        [FromBody] CreateFeeTypeRequest request, CancellationToken cancellationToken)
    {
        FeeTypeResponse fee = await _service.CreateFeeTypeAsync(request, cancellationToken);
        return Ok(ServiceResponses<FeeTypeResponse>.Ok(fee, "Fee type created."));
    }

    [HttpGet("fee-types")]
    [RequireFeature(StaffFeatureFlags.ViewInvoices)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<FeeTypeResponse>>>> ListFeeTypes(
        [FromQuery] Guid? termId, [FromQuery] string? status, [FromQuery] string? category,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FeeTypeResponse> fees = await _service.ListFeeTypesAsync(termId, status, category, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<FeeTypeResponse>>.Ok(fees, "Fee types."));
    }

    /// <summary>Owner-only: approve a pending fee type (makes it visible to parents).</summary>
    [HttpPost("fee-types/{feeTypeId:guid}/approve")]
    [RequireFeature(StaffFeatureFlags.ManageFees)]
    public async Task<ActionResult<ServiceResponses<FeeTypeResponse>>> ApproveFeeType(
        Guid feeTypeId, CancellationToken cancellationToken)
    {
        FeeTypeResponse fee = await _service.ApproveFeeTypeAsync(feeTypeId, cancellationToken);
        return Ok(ServiceResponses<FeeTypeResponse>.Ok(fee, "Fee approved."));
    }

    [HttpPost("fee-types/{feeTypeId:guid}/reject")]
    [RequireFeature(StaffFeatureFlags.ManageFees)]
    public async Task<ActionResult<ServiceResponses<FeeTypeResponse>>> RejectFeeType(
        Guid feeTypeId, [FromBody] RejectFeeTypeRequest request, CancellationToken cancellationToken)
    {
        FeeTypeResponse fee = await _service.RejectFeeTypeAsync(feeTypeId, request, cancellationToken);
        return Ok(ServiceResponses<FeeTypeResponse>.Ok(fee, "Fee rejected."));
    }

    [HttpPut("fee-types/{feeTypeId:guid}")]
    [RequireFeature(StaffFeatureFlags.ManageFees)]
    public async Task<ActionResult<ServiceResponses<FeeTypeResponse>>> UpdateFeeType(
        Guid feeTypeId, [FromBody] UpdateFeeTypeRequest request, CancellationToken cancellationToken)
    {
        FeeTypeResponse fee = await _service.UpdateFeeTypeAsync(feeTypeId, request, cancellationToken);
        return Ok(ServiceResponses<FeeTypeResponse>.Ok(fee, "Fee type updated."));
    }

    /// <summary>Hard-deletes a never-used fee type; archives one that has already billed students.</summary>
    [HttpDelete("fee-types/{feeTypeId:guid}")]
    [RequireFeature(StaffFeatureFlags.ManageFees)]
    public async Task<ActionResult<ServiceResponses<object>>> DeleteFeeType(
        Guid feeTypeId, CancellationToken cancellationToken)
    {
        bool archived = await _service.DeleteFeeTypeAsync(feeTypeId, cancellationToken);
        return Ok(ServiceResponses<object>.Ok(new { archived },
            archived ? "Fee type archived (it had already been billed)." : "Fee type deleted."));
    }

    /// <summary>Payment-based collections for a term: per fee type expected vs collected + totals.</summary>
    [HttpGet("fees/collections")]
    [RequireFeature(StaffFeatureFlags.ViewInvoices)]
    public async Task<ActionResult<ServiceResponses<BursarCollectionsResponse>>> Collections(
        [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        BursarCollectionsResponse collections = await _service.CollectionsAsync(termId, cancellationToken);
        return Ok(ServiceResponses<BursarCollectionsResponse>.Ok(collections, "Collections."));
    }
}
