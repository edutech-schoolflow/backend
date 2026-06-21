using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EduTech.Shared.Auth;

/// <summary>
/// Gates an endpoint on staff role.
/// School owners (isOwner = true) bypass this check entirely.
/// Use RequireFeatureAttribute instead when a feature flag covers the use case.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireRoleAttribute : Attribute, IAuthorizationFilter
{
    private readonly HashSet<string> _roles;

    public RequireRoleAttribute(params string[] roles)
    {
        _roles = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        System.Security.Claims.ClaimsPrincipal user = context.HttpContext.User;

        if (!user.Identity?.IsAuthenticated ?? true)
        {
            context.Result = Error(401, "Authentication required.", ErrorCodes.Unauthorized);
            return;
        }

        // School owners bypass all role checks
        if (user.FindFirst("is_owner")?.Value == "true") return;

        string? role = user.FindFirst("role")?.Value;
        if (role == null || !_roles.Contains(role))
        {
            context.Result = Error(403, "Insufficient role for this action.", ErrorCodes.AccessDenied);
        }
    }

    private static JsonResult Error(int status, string message, int code) =>
        new JsonResult(new ApiError { StatusCode = status, Message = message, ErrorCode = code })
        { StatusCode = status };
}
