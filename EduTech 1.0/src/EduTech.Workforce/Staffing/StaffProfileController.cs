using EduTech.Shared.Auth;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Workforce.Staffing;

/// <summary>
/// The signed-in staff member's own profile (with EFFECTIVE features), and the school's
/// staff-permissions matrix. Features resolve exactly like login: role → template → overrides.
/// </summary>
[ApiController]
[Authorize(Policy = "SchoolPortal")]
public sealed class StaffProfileController : ControllerBase
{
    private readonly IStaffProfileService _service;

    public StaffProfileController(IStaffProfileService service)
    {
        _service = service;
    }

    /// <summary>Me, in this school: profile + resolved features. Owners get every feature.</summary>
    [HttpGet("api/v1/staff/profile/me")]
    public async Task<ActionResult<ServiceResponses<MyStaffProfileResponse>>> Me(CancellationToken cancellationToken)
    {
        MyStaffProfileResponse profile = await _service.GetMyProfileAsync(cancellationToken);
        return Ok(ServiceResponses<MyStaffProfileResponse>.Ok(profile, "Profile."));
    }

    /// <summary>Every active staff member with their EFFECTIVE features (the permissions matrix).</summary>
    [HttpGet("api/v1/school/staff/permissions")]
    [RequireFeature(StaffFeatureFlags.ManagePermissions)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<StaffWithPermissionsResponse>>>> Permissions(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StaffWithPermissionsResponse> list = await _service.ListWithPermissionsAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<StaffWithPermissionsResponse>>.Ok(list, "Staff permissions."));
    }
}

public sealed class MyStaffProfileResponse
{
    /// <summary>Null for an owner session — owners aren't directory entries.</summary>
    public StaffDirectoryItemResponse? Staff { get; init; }
    public required IReadOnlyDictionary<string, bool> Features { get; init; }
    public required bool IsSchoolAdmin { get; init; }
}

public sealed class StaffWithPermissionsResponse
{
    public required StaffDirectoryItemResponse Staff { get; init; }
    public required IReadOnlyDictionary<string, bool> Features { get; init; }
}
