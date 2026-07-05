using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.ParentFacing;

/// <summary>Parent portal — the public school directory a parent browses to apply to a school.</summary>
[ApiController]
[Route("api/v1/parent/schools")]
[Authorize(Policy = "ParentOnly")]
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
}
