using EduTech.Shared.Auth;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EduTech.Auth.Tests.Auth;

/// <summary>The guard must 409 when the school has no current term, and pass through when it does.</summary>
public class RequiresCurrentTermAttributeTests
{
    [Fact]
    public async Task NoCurrentTerm_ShortCircuitsWith409()
    {
        ActionExecutingContext executing = BuildContext(hasTerm: false);
        bool nextCalled = false;
        ActionExecutionDelegate next = () => { nextCalled = true; return Task.FromResult(MakeExecuted(executing)); };

        await new RequiresCurrentTermAttribute().OnActionExecutionAsync(executing, next);

        Assert.False(nextCalled);
        JsonResult result = Assert.IsType<JsonResult>(executing.Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        ApiError error = Assert.IsType<ApiError>(result.Value);
        Assert.Equal(ErrorCodes.Conflict, error.ErrorCode);
    }

    [Fact]
    public async Task HasCurrentTerm_CallsNext()
    {
        ActionExecutingContext executing = BuildContext(hasTerm: true);
        bool nextCalled = false;
        ActionExecutionDelegate next = () => { nextCalled = true; return Task.FromResult(MakeExecuted(executing)); };

        await new RequiresCurrentTermAttribute().OnActionExecutionAsync(executing, next);

        Assert.True(nextCalled);
        Assert.Null(executing.Result);
    }

    private static ActionExecutingContext BuildContext(bool hasTerm)
    {
        Mock<ICurrentTermProvider> provider = new();
        provider.Setup(p => p.HasCurrentTermAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hasTerm);

        ServiceCollection services = new();
        services.AddSingleton<ICurrentTermProvider>(provider.Object);

        DefaultHttpContext http = new() { RequestServices = services.BuildServiceProvider() };
        ActionContext actionContext = new(http, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(),
            new Dictionary<string, object?>(), controller: new object());
    }

    private static ActionExecutedContext MakeExecuted(ActionExecutingContext executing)
    {
        return new ActionExecutedContext(executing, new List<IFilterMetadata>(), executing.Controller);
    }
}
