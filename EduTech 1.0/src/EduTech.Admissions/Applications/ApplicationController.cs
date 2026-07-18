using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Admissions.Applications;

/// <summary>
/// Admission applications (EDD-014 Slice 3): draft → submit → withdraw. Reads gate on Student.Read,
/// writes on Admissions.Manage. Downstream review/decision/offer/enrollment arrive in later slices.
/// </summary>
[ApiController]
[Route("api/v1/admissions/applications")]
[Authorize(Policy = "SchoolPortal")]
public sealed class ApplicationController : ControllerBase
{
    private readonly IApplicationService _service;

    public ApplicationController(IApplicationService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> Create(
        [FromBody] CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.CreateAsync(request, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Draft application created."));
    }

    [HttpGet]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ApplicationResponse>>>> List(
        [FromQuery] Guid? cycleId, [FromQuery] string? status, CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationResponse> apps = await _service.ListAsync(cycleId, status, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ApplicationResponse>>.Ok(apps, "Applications."));
    }

    [HttpGet("{applicationId:guid}")]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> Get(
        Guid applicationId, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.GetAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Application."));
    }

    [HttpPost("{applicationId:guid}/submit")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> Submit(
        Guid applicationId, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.SubmitAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Application submitted."));
    }

    [HttpPost("{applicationId:guid}/withdraw")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> Withdraw(
        Guid applicationId, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.WithdrawAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Application withdrawn."));
    }
}
