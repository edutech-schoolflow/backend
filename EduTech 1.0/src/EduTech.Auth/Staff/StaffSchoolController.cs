using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Auth.Staff;

/// <summary>
/// Multi-school endpoints for a logged-in staff member (StaffAuth). List affiliations, and switch the
/// active school — which re-mints a school-scoped access token (new sf_access cookie). The refresh
/// token is unchanged (it's the staff session, not school-scoped).
/// </summary>
[ApiController]
[Route("api/v1/staff/schools")]
[Authorize(Policy = "StaffOnly")]
public sealed class StaffSchoolController : ControllerBase
{
    private const string AccessCookie = "sf_access";

    private readonly IStaffSchoolService _service;

    public StaffSchoolController(IStaffSchoolService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<StaffSchoolItem>>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StaffSchoolItem> schools = await _service.ListMySchoolsAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<StaffSchoolItem>>.Ok(schools, "My schools."));
    }

    [HttpPost("{schoolId:guid}/switch")]
    public async Task<ActionResult<ServiceResponses<StaffAuthResponse>>> Switch(
        Guid schoolId, CancellationToken cancellationToken)
    {
        StaffSwitchResult result = await _service.SwitchAsync(schoolId, cancellationToken);

        Response.Cookies.Append(AccessCookie, result.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = result.AccessTokenExpiresAt,
            Path = "/"
        });

        return Ok(ServiceResponses<StaffAuthResponse>.Ok(
            new StaffAuthResponse { AccessTokenExpiresAt = result.AccessTokenExpiresAt },
            "Switched school."));
    }
}
