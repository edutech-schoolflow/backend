using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Grades.Subjects;

/// <summary>
/// Per-class subject catalog (SchoolPortal). Reads need can_view_student_records; catalog changes are
/// school setup and need can_manage_admissions (owner bypasses both).
/// </summary>
[ApiController]
[Authorize(Policy = "SchoolPortal")]
public sealed class SubjectController : ControllerBase
{
    private readonly ISubjectService _service;

    public SubjectController(ISubjectService service)
    {
        _service = service;
    }

    [HttpGet("api/v1/classes/{classId:guid}/subjects")]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<SubjectResponse>>>> List(
        Guid classId, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubjectResponse> subjects = await _service.ListAsync(classId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<SubjectResponse>>.Ok(subjects, "Subjects."));
    }

    [HttpGet("api/v1/classes/{classId:guid}/subjects/suggestions")]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<string>>>> Suggestions(
        Guid classId, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> names = await _service.SuggestionsAsync(classId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<string>>.Ok(names, "Suggested subjects for this level."));
    }

    [HttpPost("api/v1/classes/{classId:guid}/subjects")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<SubjectResponse>>> Create(
        Guid classId, [FromBody] CreateSubjectRequest request, CancellationToken cancellationToken)
    {
        SubjectResponse subject = await _service.CreateAsync(classId, request, cancellationToken);
        return Ok(ServiceResponses<SubjectResponse>.Ok(subject, "Subject added."));
    }

    [HttpPost("api/v1/classes/{classId:guid}/subjects/seed")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<int>>> SeedDefaults(
        Guid classId, CancellationToken cancellationToken)
    {
        int added = await _service.SeedDefaultsAsync(classId, cancellationToken);
        return Ok(ServiceResponses<int>.Ok(added, $"Seeded {added} subject(s)."));
    }

    [HttpDelete("api/v1/subjects/{id:guid}")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<string?>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Subject removed."));
    }
}
