using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.ParentFacing;

/// <summary>
/// Parent portal — a parent's own children and their academic data. School-agnostic and
/// ownership-scoped (a parent only ever sees children linked to them via parent_children).
/// </summary>
[ApiController]
[Route("api/v1/family/children")]
[Authorize(Policy = "AuthenticatedIdentity")]
public sealed class ParentChildrenController : ControllerBase
{
    private readonly IParentChildrenService _service;

    public ParentChildrenController(IParentChildrenService service)
    {
        _service = service;
    }

    /// <summary>My children + their active enrollment (school, class).</summary>
    [HttpGet]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ParentChildResponse>>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ParentChildResponse> children = await _service.GetChildrenAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ParentChildResponse>>.Ok(children, "Children."));
    }

    /// <summary>Full profile for one of my children (for prefilling the edit/enrol form).</summary>
    [HttpGet("{childProfileId:guid}")]
    public async Task<ActionResult<ServiceResponses<ChildProfileResponse>>> Get(
        Guid childProfileId, CancellationToken cancellationToken)
    {
        ChildProfileResponse child = await _service.GetChildAsync(childProfileId, cancellationToken);
        return Ok(ServiceResponses<ChildProfileResponse>.Ok(child, "Child profile."));
    }

    /// <summary>Create a new child profile, or update one I own (when Id is supplied).</summary>
    [HttpPost]
    public async Task<ActionResult<ServiceResponses<object>>> Upsert(
        [FromForm] UpsertChildProfileRequest request, CancellationToken cancellationToken)
    {
        Guid id = await _service.UpsertChildAsync(request, cancellationToken);
        return Ok(ServiceResponses<object>.Ok(new { childProfileId = id }, "Child profile saved."));
    }

    /// <summary>A child's PUBLISHED report cards.</summary>
    [HttpGet("{childProfileId:guid}/report-cards")]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ChildReportCardSummary>>>> ReportCards(
        Guid childProfileId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChildReportCardSummary> cards = await _service.GetReportCardsAsync(childProfileId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ChildReportCardSummary>>.Ok(cards, "Report cards."));
    }

    /// <summary>A child's PUBLISHED CA / exam scores (optionally for one term).</summary>
    [HttpGet("{childProfileId:guid}/ca-scores")]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ChildCaScoreResponse>>>> CaScores(
        Guid childProfileId, [FromQuery] Guid? termId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChildCaScoreResponse> scores = await _service.GetCaScoresAsync(childProfileId, termId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ChildCaScoreResponse>>.Ok(scores, "Scores."));
    }

    /// <summary>A child's attendance summary per term.</summary>
    [HttpGet("{childProfileId:guid}/attendance")]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ChildAttendanceSummary>>>> Attendance(
        Guid childProfileId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChildAttendanceSummary> summary = await _service.GetAttendanceAsync(childProfileId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ChildAttendanceSummary>>.Ok(summary, "Attendance summary."));
    }
}
