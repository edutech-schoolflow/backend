using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.School.Dashboard;

/// <summary>The workspace home for a school session (owner or staff) — live aggregates only.</summary>
[ApiController]
[Route("api/v1/school")]
[Authorize(Policy = "SchoolPortal")]
public sealed class SchoolDashboardController : ControllerBase
{
    private readonly ISchoolDashboardService _service;

    public SchoolDashboardController(ISchoolDashboardService service)
    {
        _service = service;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<ServiceResponses<SchoolDashboardResponse>>> Get(CancellationToken cancellationToken)
    {
        SchoolDashboardResponse dashboard = await _service.GetAsync(cancellationToken);
        return Ok(ServiceResponses<SchoolDashboardResponse>.Ok(dashboard, "Dashboard."));
    }
}
