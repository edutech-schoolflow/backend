using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.School.PlatformAdmin;

/// <summary>
/// Platform-admin school KYC review — the gate that activates a registered school. PlatformAdminAuth
/// + per-action sub-role checks (compliance_reviewer/super_admin) inside the service.
/// </summary>
[ApiController]
[Route("api/v1/admin/schools")]
[Authorize(Policy = "PlatformAdminOnly")]
public sealed class AdminSchoolKycController : ControllerBase
{
    private readonly ISchoolKycAdminService _kycService;

    public AdminSchoolKycController(ISchoolKycAdminService kycService)
    {
        _kycService = kycService;
    }

    [HttpGet("kyc")]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<AdminSchoolItem>>>> Queue(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AdminSchoolItem> schools = await _kycService.ListQueueAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<AdminSchoolItem>>.Ok(schools, "Schools awaiting approval."));
    }

    [HttpGet("{schoolId:guid}/kyc")]
    public async Task<ActionResult<ServiceResponses<AdminSchoolItem>>> Detail(
        Guid schoolId, CancellationToken cancellationToken)
    {
        AdminSchoolItem school = await _kycService.GetDetailAsync(schoolId, cancellationToken);
        return Ok(ServiceResponses<AdminSchoolItem>.Ok(school, "School detail."));
    }

    [HttpPost("{schoolId:guid}/kyc/approve")]
    public async Task<ActionResult<ServiceResponses<string?>>> Approve(
        Guid schoolId, [FromBody] ApproveSchoolRequest request, CancellationToken cancellationToken)
    {
        await _kycService.ApproveAsync(schoolId, request, ClientIp(), cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "School approved and activated."));
    }

    [HttpPost("{schoolId:guid}/kyc/reject")]
    public async Task<ActionResult<ServiceResponses<string?>>> Reject(
        Guid schoolId, [FromBody] RejectSchoolRequest request, CancellationToken cancellationToken)
    {
        await _kycService.RejectAsync(schoolId, request, ClientIp(), cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "School KYC rejected."));
    }

    [HttpPost("{schoolId:guid}/suspend")]
    public async Task<ActionResult<ServiceResponses<string?>>> Suspend(
        Guid schoolId, [FromBody] SuspendSchoolRequest request, CancellationToken cancellationToken)
    {
        await _kycService.SuspendAsync(schoolId, request, ClientIp(), cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "School suspended."));
    }

    private string? ClientIp()
    {
        return Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
