using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Admissions.Assessments;

/// <summary>
/// An application's assessments (EDD-014 Slice 5): schedule → record result / cancel. Typed
/// (exam/interview/observation/portfolio/external_result). Reads gate on Student.Read, writes on Admissions.Manage.
/// </summary>
[ApiController]
[Route("api/v1/admissions/applications/{applicationId:guid}/assessments")]
[Authorize(Policy = "SchoolPortal")]
public sealed class AssessmentController : ControllerBase
{
    private readonly IAssessmentService _service;

    public AssessmentController(IAssessmentService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<AssessmentResponse>>> Schedule(
        Guid applicationId, [FromBody] ScheduleAssessmentRequest request, CancellationToken cancellationToken)
    {
        AssessmentResponse a = await _service.ScheduleAsync(applicationId, request, cancellationToken);
        return Ok(ServiceResponses<AssessmentResponse>.Ok(a, "Assessment scheduled."));
    }

    [HttpGet]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<AssessmentResponse>>>> ListForApplication(
        Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<AssessmentResponse> list = await _service.ListAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<AssessmentResponse>>.Ok(list, "Assessments."));
    }

    [HttpPost("{assessmentId:guid}/reschedule")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<AssessmentResponse>>> Reschedule(
        Guid applicationId, Guid assessmentId, [FromBody] RescheduleAssessmentRequest request, CancellationToken cancellationToken)
    {
        AssessmentResponse a = await _service.RescheduleAsync(applicationId, assessmentId, request.ScheduledAt, cancellationToken);
        return Ok(ServiceResponses<AssessmentResponse>.Ok(a, "Assessment rescheduled."));
    }

    [HttpPost("{assessmentId:guid}/result")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<AssessmentResponse>>> RecordResult(
        Guid applicationId, Guid assessmentId, [FromBody] RecordResultRequest request, CancellationToken cancellationToken)
    {
        AssessmentResponse a = await _service.RecordResultAsync(applicationId, assessmentId, request, cancellationToken);
        return Ok(ServiceResponses<AssessmentResponse>.Ok(a, "Result recorded."));
    }

    [HttpPost("{assessmentId:guid}/cancel")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<AssessmentResponse>>> Cancel(
        Guid applicationId, Guid assessmentId, CancellationToken cancellationToken)
    {
        AssessmentResponse a = await _service.CancelAsync(applicationId, assessmentId, cancellationToken);
        return Ok(ServiceResponses<AssessmentResponse>.Ok(a, "Assessment cancelled."));
    }
}
