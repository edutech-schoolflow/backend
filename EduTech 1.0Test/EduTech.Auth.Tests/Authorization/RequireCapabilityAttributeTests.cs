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

namespace EduTech.Auth.Tests.Authorization;

/// <summary>
/// The capability authorization policy (EDD-006). Behavior must match the legacy
/// <c>[RequireFeature]</c> for the 13 mapped capabilities: owners bypass, wrong-portal users are
/// rejected, and a mapped capability is granted iff its legacy flag claim is "true".
/// </summary>
public class RequireCapabilityAttributeTests
{
    [Fact]
    public void Unauthenticated_Returns401()
    {
        AuthorizationFilterContext ctx = BuildContext(authenticated: false);

        new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorization(ctx);

        AssertError(ctx, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public void Owner_BypassesTheCheck()
    {
        AuthorizationFilterContext ctx = BuildContext(
            new Claim("is_owner", "true"),
            new Claim("user_type", UserTypes.School));

        new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorization(ctx);

        Assert.Null(ctx.Result); // allowed
    }

    [Theory]
    [InlineData(UserTypes.Parent)]
    [InlineData(UserTypes.PlatformAdmin)]
    public void WrongPortal_Returns403(string userType)
    {
        AuthorizationFilterContext ctx = BuildContext(
            new Claim("is_owner", "false"),
            new Claim("user_type", userType));

        new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorization(ctx);

        AssertError(ctx, StatusCodes.Status403Forbidden, ErrorCodes.AccessDenied);
    }

    [Fact]
    public void StaffWithLegacyFlagTrue_IsAllowed()
    {
#pragma warning disable CS0618 // asserting the bridge maps to this flag
        string flag = StaffFeatureFlags.ManageAdmissions;
#pragma warning restore CS0618
        AuthorizationFilterContext ctx = BuildContext(
            new Claim("is_owner", "false"),
            new Claim("user_type", UserTypes.Staff),
            new Claim(flag, "true"));

        new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorization(ctx);

        Assert.Null(ctx.Result); // allowed
    }

    [Fact]
    public void StaffWithoutLegacyFlag_Returns403()
    {
        AuthorizationFilterContext ctx = BuildContext(
            new Claim("is_owner", "false"),
            new Claim("user_type", UserTypes.Staff)); // flag claim absent

        new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorization(ctx);

        AssertError(ctx, StatusCodes.Status403Forbidden, ErrorCodes.AccessDenied);
    }

    [Fact]
    public void StaffWithLegacyFlagFalse_Returns403()
    {
#pragma warning disable CS0618
        string flag = StaffFeatureFlags.ManageAdmissions;
#pragma warning restore CS0618
        AuthorizationFilterContext ctx = BuildContext(
            new Claim("is_owner", "false"),
            new Claim("user_type", UserTypes.Staff),
            new Claim(flag, "false"));

        new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorization(ctx);

        AssertError(ctx, StatusCodes.Status403Forbidden, ErrorCodes.AccessDenied);
    }

    private static AuthorizationFilterContext BuildContext(params Claim[] claims) =>
        BuildContext(authenticated: true, claims);

    private static AuthorizationFilterContext BuildContext(bool authenticated, params Claim[] claims)
    {
        // A non-null authentication type is what makes ClaimsIdentity.IsAuthenticated true.
        ClaimsIdentity identity = authenticated
            ? new ClaimsIdentity(claims, authenticationType: "test")
            : new ClaimsIdentity(claims);
        DefaultHttpContext http = new() { User = new ClaimsPrincipal(identity) };
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
