using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.Admissions;

/// <summary>School-side admissions queue (SchoolPortal; reads view, writes manage; owner bypasses).</summary>
[ApiController]
[Route("api/v1/school/applications")]
[Authorize(Policy = "SchoolPortal")]
public sealed class SchoolApplicationController : ControllerBase
{
    private readonly ISchoolApplicationService _service;

    public SchoolApplicationController(ISchoolApplicationService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ApplicationResponse>>>> List(
        [FromQuery] string? status, CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationResponse> apps = await _service.ListAsync(status, cancellationToken);
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

    [HttpPost("{applicationId:guid}/exam")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> ScheduleExam(
        Guid applicationId, [FromBody] ScheduleExamRequest request, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.ScheduleExamAsync(applicationId, request, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Exam scheduled."));
    }

    [HttpPost("{applicationId:guid}/assessment")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> RecordAssessment(
        Guid applicationId, [FromBody] RecordAssessmentRequest request, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.RecordAssessmentAsync(applicationId, request, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Assessment recorded."));
    }

    [HttpPost("{applicationId:guid}/admit")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> Admit(
        Guid applicationId, [FromBody] AdmitApplicationRequest request, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.AdmitAsync(applicationId, request, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Applicant admitted."));
    }

    [HttpPost("{applicationId:guid}/reject")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> Reject(
        Guid applicationId, [FromBody] RejectApplicationRequest request, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.RejectAsync(applicationId, request, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Application rejected."));
    }
}
