using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Auth;

/// <summary>
/// Gates an endpoint on a capability (EDD-006/013) — the canonical way to authorize a staff action.
///
/// <para>Authorization is <b>derived, never embedded</b>: this filter asks the server-side
/// <see cref="ICapabilityResolver"/> what the current workspace (<c>context_id</c>) grants right now.
/// It reads no capability flag from the token — a permission change takes effect immediately, without
/// re-minting. It is fully actor-neutral: owners resolve to every capability, parents/admins to none,
/// staff to their resolved set — no <c>is_owner</c>/<c>user_type</c> branching here.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireCapabilityAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _capability;

    public RequireCapabilityAttribute(string capability)
    {
        _capability = capability;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        System.Security.Claims.ClaimsPrincipal user = context.HttpContext.User;

        if (!user.Identity?.IsAuthenticated ?? true)
        {
            context.Result = Error(401, "Authentication required.", ErrorCodes.Unauthorized);
            return;
        }

        // The workspace this request belongs to. No context ⇒ no workspace capabilities.
        if (!Guid.TryParse(user.FindFirst("context_id")?.Value, out Guid contextId))
        {
            context.Result = Error(403, "You do not have permission to perform this action.", ErrorCodes.AccessDenied);
            return;
        }

        ICapabilityResolver resolver = context.HttpContext.RequestServices.GetRequiredService<ICapabilityResolver>();
        bool granted = await resolver.HasCapabilityAsync(contextId, _capability, context.HttpContext.RequestAborted);
        if (!granted)
        {
            context.Result = Error(403, "You do not have permission to perform this action.", ErrorCodes.AccessDenied);
        }
    }

    private static JsonResult Error(int status, string message, int code) =>
        new JsonResult(new ApiError { StatusCode = status, Message = message, ErrorCode = code })
        { StatusCode = status };
}
