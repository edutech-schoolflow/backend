using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EduTech.Shared.Auth;

/// <summary>
/// Gates an endpoint on one of the 13 staff feature flags.
/// School owners (isOwner = true) bypass this check entirely.
/// Only valid for staff-portal endpoints (user_type = "staff" | "school").
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireFeatureAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _featureName;

    public RequireFeatureAttribute(string featureName)
    {
        _featureName = featureName;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        System.Security.Claims.ClaimsPrincipal user = context.HttpContext.User;

        if (!user.Identity?.IsAuthenticated ?? true)
        {
            context.Result = Error(401, "Authentication required.", ErrorCodes.Unauthorized);
            return;
        }

        // School owners bypass all feature checks
        if (user.FindFirst("is_owner")?.Value == "true") return;

        // Parents and platform admins have no feature flags — wrong portal
        string? userType = user.FindFirst("user_type")?.Value;
        if (userType == UserTypes.Parent || userType == UserTypes.PlatformAdmin)
        {
            context.Result = Error(403, "This endpoint is for staff only.", ErrorCodes.AccessDenied);
            return;
        }

        string? featureClaim = user.FindFirst(_featureName)?.Value;
        if (featureClaim != "true")
        {
            context.Result = Error(403,
                "You do not have permission to perform this action.",
                ErrorCodes.AccessDenied);
        }
    }

    private static JsonResult Error(int status, string message, int code) =>
        new JsonResult(new ApiError { StatusCode = status, Message = message, ErrorCode = code })
        { StatusCode = status };
}
