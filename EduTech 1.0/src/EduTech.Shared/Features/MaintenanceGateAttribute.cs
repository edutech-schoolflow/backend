using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Features;

/// <summary>
/// Blocks an endpoint (503) while auth is in maintenance mode (<c>auth.maintenance</c> flag ON).
/// Applied to the non-admin login endpoints — admins are intentionally NOT gated so they can still
/// sign in and resolve the incident.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MaintenanceGateAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        IFeatureFlagService flags = context.HttpContext.RequestServices.GetRequiredService<IFeatureFlagService>();

        bool inMaintenance = await flags.IsEnabledAsync(
            FeatureKeys.AuthMaintenance, null, context.HttpContext.RequestAborted);

        if (inMaintenance)
        {
            context.Result = new JsonResult(new ApiError
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                Message = "We're undergoing maintenance. Please try again shortly.",
                ErrorCode = ErrorCodes.MaintenanceMode
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return;
        }

        await next();
    }
}
