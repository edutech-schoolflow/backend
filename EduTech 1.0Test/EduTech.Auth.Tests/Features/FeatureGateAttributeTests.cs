using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Features;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EduTech.Auth.Tests.Features;

/// <summary>The gate must short-circuit with 503 when a feature is off, and pass through when on.</summary>
public class FeatureGateAttributeTests
{
    [Fact]
    public async Task DisabledFeature_ShortCircuitsWith503()
    {
        ActionExecutingContext executing = BuildContext(enabled: false);
        bool nextCalled = false;
        ActionExecutionDelegate next = () => { nextCalled = true; return Task.FromResult(MakeExecuted(executing)); };

        await new FeatureGateAttribute("fees").OnActionExecutionAsync(executing, next);

        Assert.False(nextCalled);
        JsonResult result = Assert.IsType<JsonResult>(executing.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        ApiError error = Assert.IsType<ApiError>(result.Value);
        Assert.Equal(ErrorCodes.FeatureDisabled, error.ErrorCode);
    }

    [Fact]
    public async Task EnabledFeature_CallsNext()
    {
        ActionExecutingContext executing = BuildContext(enabled: true);
        bool nextCalled = false;
        ActionExecutionDelegate next = () => { nextCalled = true; return Task.FromResult(MakeExecuted(executing)); };

        await new FeatureGateAttribute("fees").OnActionExecutionAsync(executing, next);

        Assert.True(nextCalled);
        Assert.Null(executing.Result);
    }

    private static ActionExecutingContext BuildContext(bool enabled)
    {
        Mock<IFeatureFlagService> flags = new();
        flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(enabled);

        Mock<IEduTechRequestContext> requestContext = new();
        requestContext.Setup(c => c.SchoolId).Returns((string?)null);

        ServiceCollection services = new();
        services.AddSingleton<IFeatureFlagService>(flags.Object);
        services.AddSingleton<IEduTechRequestContext>(requestContext.Object);

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
