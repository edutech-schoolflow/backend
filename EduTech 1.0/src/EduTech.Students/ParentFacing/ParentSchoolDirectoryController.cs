using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.ParentFacing;

/// <summary>
/// The public school directory browsed during discovery. Discovery is IDENTITY-scoped (EDD-002): you
/// find a school before you belong to one, so any authenticated person may browse — not only a parent
/// context. The data is public-listed schools regardless, so no persona is needed.
/// </summary>
[ApiController]
[Route("api/v1/family/schools")]
[Authorize(Policy = "AuthenticatedIdentity")]
public sealed class ParentSchoolDirectoryController : ControllerBase
{
    private readonly IParentSchoolDirectoryService _service;

    public ParentSchoolDirectoryController(IParentSchoolDirectoryService service)
    {
        _service = service;
    }

    /// <summary>Public, listed schools — optionally filtered by name/location text and by type.</summary>
    [HttpGet]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ParentSchoolListItem>>>> Search(
        [FromQuery] string? query, [FromQuery] string? type, CancellationToken cancellationToken)
    {
        IReadOnlyList<ParentSchoolListItem> schools = await _service.SearchAsync(query, type, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ParentSchoolListItem>>.Ok(schools, "Schools."));
    }

    /// <summary>A single public school's profile.</summary>
    [HttpGet("{schoolId:guid}")]
    public async Task<ActionResult<ServiceResponses<ParentSchoolProfileResponse>>> Get(
        Guid schoolId, CancellationToken cancellationToken)
    {
        ParentSchoolProfileResponse school = await _service.GetAsync(schoolId, cancellationToken);
        return Ok(ServiceResponses<ParentSchoolProfileResponse>.Ok(school, "School."));
    }

    /// <summary>The classes a public school offers — the parent's desired-class options when applying.</summary>
    [HttpGet("{schoolId:guid}/classes")]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ParentSchoolClass>>>> Classes(
        Guid schoolId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ParentSchoolClass> classes = await _service.GetClassesAsync(schoolId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ParentSchoolClass>>.Ok(classes, "School classes."));
    }
}
