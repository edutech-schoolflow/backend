using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Grades.ReportCards;

/// <summary>The school's grading scale (SchoolPortal). Read needs can_view_student_records; saving
/// the scale is school setup and needs can_manage_admissions (owner bypasses both).</summary>
[ApiController]
[Authorize(Policy = "SchoolPortal")]
public sealed class GradingScaleController : ControllerBase
{
    private readonly IGradingScaleService _service;

    public GradingScaleController(IGradingScaleService service)
    {
        _service = service;
    }

    [HttpGet("api/v1/grading-scale")]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<GradeBoundaryDto>>>> Get(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GradeBoundaryDto> scale = await _service.GetAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<GradeBoundaryDto>>.Ok(scale, "Grading scale."));
    }

    [HttpPut("api/v1/grading-scale")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<string?>>> Save(
        [FromBody] SaveGradingScaleRequest request, CancellationToken cancellationToken)
    {
        await _service.SaveAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Grading scale saved."));
    }
}
