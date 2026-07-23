using System.Security.Claims;
using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EduTech.Auth.Tests.Authorization;

/// <summary>
/// The capability authorization filter (EDD-013). It is now fully actor-neutral: it reads only the
/// workspace (<c>context_id</c>) and asks the server-side <see cref="ICapabilityResolver"/> — no
/// <c>is_owner</c>/<c>user_type</c>/flag-claim reads. (Owner=all, parent=∅ etc. are the resolver's
/// job, covered by CapabilityResolverTests.)
/// </summary>
public class RequireCapabilityAttributeTests
{
    private static readonly Guid Ctx = Guid.NewGuid();

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        AuthorizationFilterContext ctx = BuildContext(authenticated: false, granted: false);
        await new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorizationAsync(ctx);
        AssertError(ctx, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task NoContextId_Returns403()
    {
        AuthorizationFilterContext ctx = BuildContext(authenticated: true, granted: true, includeContext: false);
        await new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorizationAsync(ctx);
        AssertError(ctx, StatusCodes.Status403Forbidden, ErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task ResolverGrants_IsAllowed()
    {
        AuthorizationFilterContext ctx = BuildContext(authenticated: true, granted: true);
        await new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorizationAsync(ctx);
        Assert.Null(ctx.Result); // allowed
    }

    [Fact]
    public async Task ResolverDenies_Returns403()
    {
        AuthorizationFilterContext ctx = BuildContext(authenticated: true, granted: false);
        await new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorizationAsync(ctx);
        AssertError(ctx, StatusCodes.Status403Forbidden, ErrorCodes.AccessDenied);
    }

    private static AuthorizationFilterContext BuildContext(bool authenticated, bool granted, bool includeContext = true)
    {
        List<Claim> claims = new();
        if (includeContext) claims.Add(new Claim("context_id", Ctx.ToString()));
        // A non-null authentication type is what makes ClaimsIdentity.IsAuthenticated true.
        ClaimsIdentity identity = authenticated ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity(claims);

        Mock<ICapabilityResolver> resolver = new();
        resolver.Setup(r => r.HasCapabilityAsync(Ctx, Capabilities.Admissions.Manage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(granted);

        ServiceProvider sp = new ServiceCollection().AddSingleton(resolver.Object).BuildServiceProvider();
        DefaultHttpContext http = new() { User = new ClaimsPrincipal(identity), RequestServices = sp };
        ActionContext actionContext = new(http, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    private static void AssertError(AuthorizationFilterContext ctx, int status, int errorCode)
    {
        JsonResult result = Assert.IsType<JsonResult>(ctx.Result);
        Assert.Equal(status, result.StatusCode);
        ApiError error = Assert.IsType<ApiError>(result.Value);
        Assert.Equal(errorCode, error.ErrorCode);
    }
}
