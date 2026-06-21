using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Features;

/// <summary>
/// Gates an endpoint on a RELEASE feature flag (operational on/off), resolved per the caller's school
/// (override → global). When the feature is off, short-circuits with 503. Distinct from
/// <c>[RequireFeature]</c>, which gates on a staff member's PERMISSION.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class FeatureGateAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _key;

    public FeatureGateAttribute(string key)
    {
        _key = key;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        IFeatureFlagService flags = context.HttpContext.RequestServices.GetRequiredService<IFeatureFlagService>();
        IEduTechRequestContext requestContext =
            context.HttpContext.RequestServices.GetRequiredService<IEduTechRequestContext>();

        Guid? schoolId = Guid.TryParse(requestContext.SchoolId, out Guid sid) ? sid : null;

        bool enabled = await flags.IsEnabledAsync(_key, schoolId, context.HttpContext.RequestAborted);
        if (!enabled)
        {
            context.Result = new JsonResult(new ApiError
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                Message = "This feature is currently unavailable.",
                ErrorCode = ErrorCodes.FeatureDisabled
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return;
        }

        await next();
    }
}
