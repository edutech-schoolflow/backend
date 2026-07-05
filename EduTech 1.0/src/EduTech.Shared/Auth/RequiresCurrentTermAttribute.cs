using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Auth;

/// <summary>
/// Gates a term-scoped write (attendance, grades, report cards…) on the school having a current term set.
/// Without one the school isn't "in session", so recording term-based data is refused with a 409 — the same
/// invariant the calendar enforces, applied uniformly at the edge (mirrors <see cref="RequireFeatureAttribute"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresCurrentTermAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ICurrentTermProvider provider =
            context.HttpContext.RequestServices.GetRequiredService<ICurrentTermProvider>();

        if (!await provider.HasCurrentTermAsync(context.HttpContext.RequestAborted))
        {
            context.Result = new JsonResult(new ApiError
            {
                StatusCode = StatusCodes.Status409Conflict,
                Message = "No active term. Set the current term in the Academic Calendar before recording this.",
                ErrorCode = ErrorCodes.Conflict
            })
            { StatusCode = StatusCodes.Status409Conflict };
            return;
        }

        await next();
    }
}
