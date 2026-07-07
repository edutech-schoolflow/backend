using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using EduTech.Students.Classes.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.Classes;

/// <summary>The standard class levels (the 6-3-3 ladder). Platform reference data — the same list every
/// portal uses, so the frontend never hardcodes it.</summary>
public sealed class ClassLevelResponse
{
    public required string Name { get; init; }
    public required string Stage { get; init; }   // nursery | primary | junior_secondary | senior_secondary
    public required int Order { get; init; }
}

[ApiController]
[AllowAnonymous] // static reference data — not sensitive, used by both the school and parent portals
public sealed class ClassLevelController : ControllerBase
{
    [HttpGet("api/v1/class-levels")]
    public ActionResult<ServiceResponses<IReadOnlyList<ClassLevelResponse>>> Get()
    {
        IReadOnlyList<ClassLevelResponse> levels = NigerianEducationLadder.All
            .Select(g => new ClassLevelResponse
            {
                Name = g.Name,
                Stage = SnakeCaseEnum.ToWire(g.Stage),
                Order = g.Order
            })
            .ToList();

        return Ok(ServiceResponses<IReadOnlyList<ClassLevelResponse>>.Ok(levels, "Class levels."));
    }
}
