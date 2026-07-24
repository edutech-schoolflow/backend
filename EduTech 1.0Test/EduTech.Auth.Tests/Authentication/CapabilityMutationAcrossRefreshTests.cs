using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EduTech.Auth.Tests.Authentication;

/// <summary>
/// EDD-012 B2c.3c — the deep invariant behind refresh re-keying: refresh renews identity + context, it
/// never embeds or caches authorization. Two halves prove it:
///   (1) the context token refresh re-mints carries NO authorization — only identity/context;
///   (2) the same context, after a permission change, is decided live by the resolver — allow → deny,
///       no re-login. Together: a permission removed after login takes effect on the very next request,
///       whether or not the token was refreshed (refresh just re-mints the same authorization-free token).
/// </summary>
public class CapabilityMutationAcrossRefreshTests
{
    private const string Key = "test-signing-key-that-is-at-least-sixty-four-bytes-long-for-hmac512!!";
    private static readonly Guid Ctx = Guid.NewGuid();

    [Fact]
    public void RefreshedContextToken_CarriesNoAuthorization()
    {
        // Exactly what MintContextAccessAsync issues for a staff context — there is no way to put
        // authorization in it (the mint has no feature input since B2c.3d).
        string token = TokenVendor.VendStaffScopedToken(Key, "EduTech", "EduTechApp",
            Guid.NewGuid().ToString(), "+2348010000000", Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            StaffRoles.Teacher, "full_time", "approved",
            identityId: Guid.NewGuid().ToString(), contextId: Ctx.ToString(),
            membershipId: Guid.NewGuid().ToString(), organizationId: Guid.NewGuid().ToString());

        List<Claim> claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();

        // It locates the workspace...
        Assert.Contains(claims, c => c.Type == "context_id" && c.Value == Ctx.ToString());
        // ...but embeds zero authorization — no capability flags (even the one passed in), no capability set.
        Assert.DoesNotContain(claims, c => c.Type.StartsWith("can_"));
        Assert.DoesNotContain(claims, c => c.Type == "capabilities");
    }

    [Fact]
    public async Task PermissionRemovedAfterLogin_NextRequestDenied_WithNoRelogin()
    {
        // The SAME workspace token (same context_id) across a permission change. Authorization is asked of
        // the resolver on every request, so a refresh — which re-mints the same context_id — changes nothing.
        Assert.Null((await Evaluate(granted: true)).Result);                      // granted → allowed

        JsonResult denied = Assert.IsType<JsonResult>((await Evaluate(granted: false)).Result);
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);         // removed → denied, same token
    }

    private static async Task<AuthorizationFilterContext> Evaluate(bool granted)
    {
        ClaimsIdentity identity = new(new[] { new Claim("context_id", Ctx.ToString()) }, "test");
        Mock<ICapabilityResolver> resolver = new();
        resolver.Setup(r => r.HasCapabilityAsync(Ctx, Capabilities.Admissions.Manage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(granted);

        ServiceProvider sp = new ServiceCollection().AddSingleton(resolver.Object).BuildServiceProvider();
        DefaultHttpContext http = new() { User = new ClaimsPrincipal(identity), RequestServices = sp };
        AuthorizationFilterContext ctx = new(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());

        await new RequireCapabilityAttribute(Capabilities.Admissions.Manage).OnAuthorizationAsync(ctx);
        return ctx;
    }
}
