using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Grades.Scores;

/// <summary>
/// Term score entry (SchoolPortal, can_enter_grades; owner bypasses). Who may enter is level-dependent:
/// the class teacher for primary-tier arms, the subject teacher for secondary arms.
/// </summary>
[ApiController]
[Authorize(Policy = "SchoolPortal")]
public sealed class GradeController : ControllerBase
{
    private readonly IGradeService _service;

    public GradeController(IGradeService service)
    {
        _service = service;
    }

    /// <summary>Arms the caller may enter grades for (class-teacher arms + subject-teacher arms).</summary>
    [HttpGet("api/v1/grades/arms")]
    [RequireCapability(Capabilities.Grades.Enter)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<GradeableArmResponse>>>> Arms(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GradeableArmResponse> arms = await _service.ListGradeableArmsAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<GradeableArmResponse>>.Ok(arms, "Gradeable arms."));
    }

    /// <summary>The score grid for (arm, subject, term, assessment), pre-filled with existing scores.</summary>
    [HttpGet("api/v1/grades/record")]
    [RequireCapability(Capabilities.Grades.Enter)]
    public async Task<ActionResult<ServiceResponses<GradeRecordResponse>>> Record(
        [FromQuery] Guid armId, [FromQuery] Guid subjectId, [FromQuery] Guid termId,
        [FromQuery] string? assessmentType, CancellationToken cancellationToken)
    {
        AssessmentType? assessment =
            SnakeCaseEnum.TryParse(assessmentType, out AssessmentType parsed) ? parsed : (AssessmentType?)null;

        GradeRecordResponse record = await _service.GetRecordAsync(armId, subjectId, termId, assessment, cancellationToken);
        return Ok(ServiceResponses<GradeRecordResponse>.Ok(record, "Grade record."));
    }

    /// <summary>Submit (or re-submit, replacing) the scores for an assessment column.</summary>
    [HttpPost("api/v1/grades")]
    [RequireCapability(Capabilities.Grades.Enter)]
    [RequiresCurrentTerm]
    public async Task<ActionResult<ServiceResponses<GradeRecordSummaryResponse>>> Submit(
        [FromBody] SubmitGradesRequest request, CancellationToken cancellationToken)
    {
        GradeRecordSummaryResponse summary = await _service.SubmitAsync(request, cancellationToken);
        return Ok(ServiceResponses<GradeRecordSummaryResponse>.Ok(summary, "Scores saved."));
    }

    /// <summary>Publish one record (draft → published, visible to parents).</summary>
    [HttpPost("api/v1/grades/{recordId:guid}/publish")]
    [RequireCapability(Capabilities.Grades.Enter)]
    public async Task<ActionResult<ServiceResponses<string?>>> Publish(Guid recordId, CancellationToken cancellationToken)
    {
        await _service.PublishAsync(recordId, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Grades published."));
    }

    /// <summary>
    /// Publish every draft record for a term (optionally scoped to one arm). A school- or arm-wide
    /// release crosses other teachers' records, so it is a leadership action (owner bypasses roles).
    /// </summary>
    [HttpPost("api/v1/grades/publish")]
    [RequireRole(StaffRoles.Principal, StaffRoles.VicePrincipal, StaffRoles.SchoolAdmin)]
    public async Task<ActionResult<ServiceResponses<int>>> PublishAll(
        [FromBody] PublishAllRequest request, CancellationToken cancellationToken)
    {
        int published = await _service.PublishAllAsync(request, cancellationToken);
        return Ok(ServiceResponses<int>.Ok(published, $"Published {published} record(s)."));
    }

    /// <summary>School-wide grades board for a term (per-record averages + pass/fail).</summary>
    [HttpGet("api/v1/grades/overview")]
    [RequireCapability(Capabilities.Grades.Enter)]
    public async Task<ActionResult<ServiceResponses<GradesOverviewResponse>>> Overview(
        [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        GradesOverviewResponse overview = await _service.GetOverviewAsync(termId, cancellationToken);
        return Ok(ServiceResponses<GradesOverviewResponse>.Ok(overview, "Grades overview."));
    }
}
