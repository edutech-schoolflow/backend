using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Features;

/// <summary>
/// Blocks a self-signup endpoint (403) when registration is switched off from the CMS — either the
/// global <c>auth.signups_disabled</c> kill-switch or the per-actor one
/// (<c>auth.signups_disabled.{actor}</c>). Kill-switch polarity: flag ON = signups closed.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SignupGateAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _actorType;

    public SignupGateAttribute(string actorType)
    {
        _actorType = actorType;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        IFeatureFlagService flags = context.HttpContext.RequestServices.GetRequiredService<IFeatureFlagService>();
        CancellationToken cancellationToken = context.HttpContext.RequestAborted;

        bool disabled = await flags.IsEnabledAsync(FeatureKeys.AuthSignupsDisabled, null, cancellationToken)
            || await flags.IsEnabledAsync($"{FeatureKeys.AuthSignupsDisabled}.{_actorType}", null, cancellationToken);

        if (disabled)
        {
            context.Result = new JsonResult(new ApiError
            {
                StatusCode = StatusCodes.Status403Forbidden,
                Message = "Registration is currently closed.",
                ErrorCode = ErrorCodes.RegistrationClosed
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
