using EduTech.Shared.Constants;
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

/// <summary>
/// The auth kill-switch gates: SignupGate (403 when signups disabled, global or per-actor) and
/// MaintenanceGate (503 when in maintenance). Both pass through when their flags are off.
/// </summary>
public class AuthGateAttributeTests
{
    // ── SignupGate ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SignupGate_GlobalDisabled_Returns403()
    {
        ActionExecutingContext ctx = BuildContext(flag =>
            flag == FeatureKeys.AuthSignupsDisabled); // global off-switch ON
        bool nextCalled = false;

        await new SignupGateAttribute("parent").OnActionExecutionAsync(ctx, Next(ctx, () => nextCalled = true));

        Assert.False(nextCalled);
        AssertApiError(ctx, StatusCodes.Status403Forbidden, ErrorCodes.RegistrationClosed);
    }

    [Fact]
    public async Task SignupGate_PerActorDisabled_Returns403()
    {
        ActionExecutingContext ctx = BuildContext(flag =>
            flag == $"{FeatureKeys.AuthSignupsDisabled}.staff"); // only staff disabled
        bool nextCalled = false;

        await new SignupGateAttribute("staff").OnActionExecutionAsync(ctx, Next(ctx, () => nextCalled = true));

        Assert.False(nextCalled);
        AssertApiError(ctx, StatusCodes.Status403Forbidden, ErrorCodes.RegistrationClosed);
    }

    [Fact]
    public async Task SignupGate_AllOff_PassesThrough()
    {
        ActionExecutingContext ctx = BuildContext(_ => false);
        bool nextCalled = false;

        await new SignupGateAttribute("school_owner").OnActionExecutionAsync(ctx, Next(ctx, () => nextCalled = true));

        Assert.True(nextCalled);
        Assert.Null(ctx.Result);
    }

    // ── MaintenanceGate ────────────────────────────────────────────────────────

    [Fact]
    public async Task MaintenanceGate_On_Returns503()
    {
        ActionExecutingContext ctx = BuildContext(flag => flag == FeatureKeys.AuthMaintenance);
        bool nextCalled = false;

        await new MaintenanceGateAttribute().OnActionExecutionAsync(ctx, Next(ctx, () => nextCalled = true));

        Assert.False(nextCalled);
        AssertApiError(ctx, StatusCodes.Status503ServiceUnavailable, ErrorCodes.MaintenanceMode);
    }

    [Fact]
    public async Task MaintenanceGate_Off_PassesThrough()
    {
        ActionExecutingContext ctx = BuildContext(_ => false);
        bool nextCalled = false;

        await new MaintenanceGateAttribute().OnActionExecutionAsync(ctx, Next(ctx, () => nextCalled = true));

        Assert.True(nextCalled);
        Assert.Null(ctx.Result);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static ActionExecutingContext BuildContext(Func<string, bool> enabledFlags)
    {
        Mock<IFeatureFlagService> flags = new();
        flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, Guid? _, CancellationToken _) => enabledFlags(key));

        ServiceCollection services = new();
        services.AddSingleton<IFeatureFlagService>(flags.Object);

        DefaultHttpContext http = new() { RequestServices = services.BuildServiceProvider() };
        ActionContext actionContext = new(http, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(),
            new Dictionary<string, object?>(), controller: new object());
    }

    private static ActionExecutionDelegate Next(ActionExecutingContext ctx, Action onCalled)
    {
        return () =>
        {
            onCalled();
            return Task.FromResult(new ActionExecutedContext(ctx, new List<IFilterMetadata>(), ctx.Controller));
        };
    }

    private static void AssertApiError(ActionExecutingContext ctx, int expectedStatus, int expectedCode)
    {
        JsonResult result = Assert.IsType<JsonResult>(ctx.Result);
        Assert.Equal(expectedStatus, result.StatusCode);
        ApiError error = Assert.IsType<ApiError>(result.Value);
        Assert.Equal(expectedCode, error.ErrorCode);
    }
}
