using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using EduTech.Shared.Constants;

namespace EduTech.Shared.Context;

public class EduTechRequestContext : IEduTechRequestContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public EduTechRequestContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? UserId => User?.FindFirst("user_id")?.Value;
    public string? UserType => User?.FindFirst("user_type")?.Value;
    public string? Role => User?.FindFirst("role")?.Value;
    public string? SchoolId => User?.FindFirst("school_id")?.Value;
    public string? AffiliationId => User?.FindFirst("affiliation_id")?.Value;
    public string? IdentityId => User?.FindFirst("identity_id")?.Value;
    public string? ContextId => User?.FindFirst("context_id")?.Value;
    public string? MembershipId => User?.FindFirst("membership_id")?.Value;
    public string? OrganizationId => User?.FindFirst("organization_id")?.Value;
    public bool IsOwner => User?.FindFirst("is_owner")?.Value == "true";

    public bool IsStaff => UserType == UserTypes.Staff;
    public bool IsSchoolOwner => UserType == UserTypes.School;
    public bool IsParent => UserType == UserTypes.Parent;
    public bool IsPlatformAdmin => UserType == UserTypes.PlatformAdmin;
}
