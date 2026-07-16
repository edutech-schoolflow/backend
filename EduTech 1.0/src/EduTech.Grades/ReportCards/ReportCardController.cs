using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Grades.ReportCards;

/// <summary>
/// School-side report cards (SchoolPortal). Reads need can_view_student_records. The subject grades,
/// totals and attendance summary are computed; comments/behavioral/status are stored.
/// </summary>
[ApiController]
[Authorize(Policy = "SchoolPortal")]
public sealed class ReportCardController : ControllerBase
{
    private readonly IReportCardService _service;

    public ReportCardController(IReportCardService service)
    {
        _service = service;
    }

    /// <summary>Report-card list for an arm + term (name, overall average, status).</summary>
    [HttpGet("api/v1/report-cards")]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ReportSummaryResponse>>>> List(
        [FromQuery] Guid armId, [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportSummaryResponse> rows = await _service.ListForArmAsync(armId, termId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ReportSummaryResponse>>.Ok(rows, "Report cards."));
    }

    /// <summary>The full computed report card for a student + term.</summary>
    [HttpGet("api/v1/report-cards/{studentId:guid}")]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<ReportCardResponse>>> Get(
        Guid studentId, [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        ReportCardResponse report = await _service.GetReportAsync(studentId, termId, cancellationToken);
        return Ok(ServiceResponses<ReportCardResponse>.Ok(report, "Report card."));
    }

    /// <summary>Save a report's comments, behavioral ratings and resumption date (draft only).</summary>
    [HttpPut("api/v1/report-cards/{studentId:guid}")]
    [RequireCapability(Capabilities.Grades.Enter)]
    public async Task<ActionResult<ServiceResponses<string?>>> SaveMeta(
        Guid studentId, [FromQuery] Guid termId, [FromBody] SaveReportMetaRequest request,
        CancellationToken cancellationToken)
    {
        await _service.SaveMetaAsync(studentId, termId, request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Report card saved."));
    }

    /// <summary>
    /// Publish one student's report card (releases it + SMSes the guardians). Releasing results is a
    /// leadership action, not an admissions one — leadership roles only (the owner bypasses roles).
    /// </summary>
    [HttpPost("api/v1/report-cards/{studentId:guid}/publish")]
    [RequireRole(StaffRoles.Principal, StaffRoles.VicePrincipal, StaffRoles.SchoolAdmin)]
    public async Task<ActionResult<ServiceResponses<string?>>> Publish(
        Guid studentId, [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        await _service.PublishStudentAsync(studentId, termId, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Report card published."));
    }

    /// <summary>Publish every (draft) report card for an arm + term, notifying guardians. Leadership only.</summary>
    [HttpPost("api/v1/report-cards/publish")]
    [RequireRole(StaffRoles.Principal, StaffRoles.VicePrincipal, StaffRoles.SchoolAdmin)]
    public async Task<ActionResult<ServiceResponses<int>>> PublishArm(
        [FromBody] PublishArmReportsRequest request, CancellationToken cancellationToken)
    {
        int published = await _service.PublishArmAsync(request, cancellationToken);
        return Ok(ServiceResponses<int>.Ok(published, $"Published {published} report card(s)."));
    }
}
