using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Admissions.Cycles;

/// <summary>
/// Admission cycles (EDD-014 Slice 1). School-side; reads gate on Student.Read, writes on
/// Admissions.Manage (owner bypasses). Authorization is resolved server-side by the capability model.
/// </summary>
[ApiController]
[Route("api/v1/admissions/cycles")]
[Authorize(Policy = "SchoolPortal")]
public sealed class AdmissionCycleController : ControllerBase
{
    private readonly IAdmissionCycleService _service;

    public AdmissionCycleController(IAdmissionCycleService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<AdmissionCycleResponse>>> Create(
        [FromBody] CreateAdmissionCycleRequest request, CancellationToken cancellationToken)
    {
        AdmissionCycleResponse cycle = await _service.CreateAsync(request, cancellationToken);
        return Ok(ServiceResponses<AdmissionCycleResponse>.Ok(cycle, "Admission cycle created."));
    }

    [HttpGet]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<AdmissionCycleResponse>>>> List(
        [FromQuery] string? status, CancellationToken cancellationToken)
    {
        IReadOnlyList<AdmissionCycleResponse> cycles = await _service.ListAsync(status, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<AdmissionCycleResponse>>.Ok(cycles, "Admission cycles."));
    }

    [HttpGet("{cycleId:guid}")]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<AdmissionCycleResponse>>> Get(
        Guid cycleId, CancellationToken cancellationToken)
    {
        AdmissionCycleResponse cycle = await _service.GetAsync(cycleId, cancellationToken);
        return Ok(ServiceResponses<AdmissionCycleResponse>.Ok(cycle, "Admission cycle."));
    }

    [HttpPost("{cycleId:guid}/open")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<AdmissionCycleResponse>>> Open(
        Guid cycleId, CancellationToken cancellationToken)
    {
        AdmissionCycleResponse cycle = await _service.OpenAsync(cycleId, cancellationToken);
        return Ok(ServiceResponses<AdmissionCycleResponse>.Ok(cycle, "Admission cycle opened."));
    }

    [HttpPost("{cycleId:guid}/close")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<AdmissionCycleResponse>>> Close(
        Guid cycleId, CancellationToken cancellationToken)
    {
        AdmissionCycleResponse cycle = await _service.CloseAsync(cycleId, cancellationToken);
        return Ok(ServiceResponses<AdmissionCycleResponse>.Ok(cycle, "Admission cycle closed."));
    }

    [HttpPost("{cycleId:guid}/archive")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<AdmissionCycleResponse>>> Archive(
        Guid cycleId, CancellationToken cancellationToken)
    {
        AdmissionCycleResponse cycle = await _service.ArchiveAsync(cycleId, cancellationToken);
        return Ok(ServiceResponses<AdmissionCycleResponse>.Ok(cycle, "Admission cycle archived."));
    }

    [HttpPost("{cycleId:guid}/quota")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<AdmissionCycleResponse>>> SetQuota(
        Guid cycleId, [FromBody] SetQuotaRequest request, CancellationToken cancellationToken)
    {
        AdmissionCycleResponse cycle = await _service.SetQuotaAsync(cycleId, request.Quota, cancellationToken);
        return Ok(ServiceResponses<AdmissionCycleResponse>.Ok(cycle, "Quota updated."));
    }
}
