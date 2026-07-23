using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Admissions.Enrollments;

/// <summary>
/// An application's enrollment (EDD-014 Slice 8) — the platform transition. Enrolling an accepted
/// application raises StudentEnrolled (the handoff to Students). One enrollment per application.
/// Reads gate on Student.Read, writes on Admissions.Manage.
/// </summary>
[ApiController]
[Route("api/v1/admissions/applications/{applicationId:guid}/enrollment")]
[Authorize(Policy = "SchoolPortal")]
public sealed class EnrollmentController : ControllerBase
{
    private readonly IEnrollmentService _service;

    public EnrollmentController(IEnrollmentService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<EnrollmentResponse>>> Enroll(
        Guid applicationId, CancellationToken cancellationToken)
    {
        EnrollmentResponse enrollment = await _service.EnrollAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<EnrollmentResponse>.Ok(enrollment, "Student enrolled."));
    }

    [HttpGet]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<EnrollmentResponse>>> Get(
        Guid applicationId, CancellationToken cancellationToken)
    {
        EnrollmentResponse enrollment = await _service.GetAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<EnrollmentResponse>.Ok(enrollment, "Enrollment."));
    }

    [HttpPost("cancel")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<EnrollmentResponse>>> Cancel(
        Guid applicationId, [FromBody] CancelEnrollmentRequest request, CancellationToken cancellationToken)
    {
        EnrollmentResponse enrollment = await _service.CancelAsync(applicationId, request.Reason, cancellationToken);
        return Ok(ServiceResponses<EnrollmentResponse>.Ok(enrollment, "Enrollment cancelled."));
    }
}
