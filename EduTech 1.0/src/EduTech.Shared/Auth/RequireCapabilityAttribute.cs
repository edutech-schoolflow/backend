using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EduTech.Shared.Auth;

/// <summary>
/// Gates an endpoint on a capability (EDD-006) — the canonical way to authorize a staff action.
/// School owners (isOwner = true) bypass the check entirely. Only valid for staff-portal endpoints
/// (user_type = "staff" | "school").
///
/// <para>During the strangler migration the token still carries the 13 <c>can_*</c> feature flags,
/// so this filter resolves the capability's <see cref="CapabilityDefinition.LegacyFlag"/> via
/// <see cref="CapabilityRegistry"/> and checks that claim. When Sprint B introduces the server-side
/// resolver, this filter re-points at the resolved capability set — the endpoints don't change.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireCapabilityAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _capability;

    public RequireCapabilityAttribute(string capability)
    {
        _capability = capability;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        System.Security.Claims.ClaimsPrincipal user = context.HttpContext.User;

        if (!user.Identity?.IsAuthenticated ?? true)
        {
            context.Result = Error(401, "Authentication required.", ErrorCodes.Unauthorized);
            return;
        }

        // School owners hold every capability implicitly.
        if (user.FindFirst("is_owner")?.Value == "true") return;

        // Parents and platform admins have no staff capabilities — wrong portal.
        string? userType = user.FindFirst("user_type")?.Value;
        if (userType == UserTypes.Parent || userType == UserTypes.PlatformAdmin)
        {
            context.Result = Error(403, "This endpoint is for staff only.", ErrorCodes.AccessDenied);
            return;
        }

        // Bridge: the capability's legacy flag is the claim the token currently carries (Sprint A).
        string? legacyFlag = CapabilityRegistry.LegacyFlagFor(_capability);
        if (legacyFlag is null)
        {
            // A capability with no legacy flag can't be granted by today's token — it belongs to a
            // later sprint's server-side resolver. Deny rather than silently allow.
            context.Result = Error(403,
                "You do not have permission to perform this action.", ErrorCodes.AccessDenied);
            return;
        }

        if (user.FindFirst(legacyFlag)?.Value != "true")
        {
            context.Result = Error(403,
                "You do not have permission to perform this action.", ErrorCodes.AccessDenied);
        }
    }

    private static JsonResult Error(int status, string message, int code) =>
        new JsonResult(new ApiError { StatusCode = status, Message = message, ErrorCode = code })
        { StatusCode = status };
}
