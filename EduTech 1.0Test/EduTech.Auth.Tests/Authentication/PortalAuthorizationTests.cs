using System.Security.Claims;
using EduTech.Shared.Auth;
using EduTech.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Auth.Tests.Authentication;

/// <summary>
/// EDD-012 B2c.3a — with one signing key, portal isolation is AUTHORIZATION, not cryptography. These
/// evaluate the real SchoolPortal / ComplianceActor gates (RequireAuthenticatedUser + the explicit
/// user_type assertion, sharing the exact <see cref="PortalGates"/> predicates Program.cs uses) through
/// <see cref="IAuthorizationService"/>, proving they admit/deny exactly the personas the per-portal
/// signing keys used to — no more, no less. This is the behavior the key unification must preserve.
/// </summary>
public class PortalAuthorizationTests
{
    private static readonly IAuthorizationService Authz =
        new ServiceCollection().AddAuthorizationCore().AddLogging()
            .BuildServiceProvider().GetRequiredService<IAuthorizationService>();

    private static readonly AuthorizationPolicy SchoolPortal = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => PortalGates.IsSchoolOrStaff(ctx.User))
        .Build();

    private static readonly AuthorizationPolicy ComplianceActor = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => PortalGates.IsStaffOrParent(ctx.User))
        .Build();

    // A non-null authentication type is what makes ClaimsIdentity.IsAuthenticated true.
    private static ClaimsPrincipal Persona(string? userType, bool authenticated = true)
    {
        Claim[] claims = userType is null ? Array.Empty<Claim>() : new[] { new Claim("user_type", userType) };
        return new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity(claims));
    }

    private static async Task<bool> Allows(AuthorizationPolicy policy, ClaimsPrincipal user) =>
        (await Authz.AuthorizeAsync(user, policy)).Succeeded;

    [Theory]
    [InlineData(UserTypes.School, true)]
    [InlineData(UserTypes.Staff, true)]
    [InlineData(UserTypes.Parent, false)]
    [InlineData("identity", false)]
    [InlineData(UserTypes.PlatformAdmin, false)]
    public async Task SchoolPortal_AdmitsOnlyOwnerAndStaff(string userType, bool allowed) =>
        Assert.Equal(allowed, await Allows(SchoolPortal, Persona(userType)));

    [Theory]
    [InlineData(UserTypes.Staff, true)]
    [InlineData(UserTypes.Parent, true)]
    [InlineData(UserTypes.School, false)]
    [InlineData("identity", false)]
    public async Task ComplianceActor_AdmitsOnlyStaffAndParent(string userType, bool allowed) =>
        Assert.Equal(allowed, await Allows(ComplianceActor, Persona(userType)));

    [Fact]
    public async Task Unauthenticated_IsAlwaysDenied()
    {
        Assert.False(await Allows(SchoolPortal, Persona(UserTypes.Staff, authenticated: false)));
        Assert.False(await Allows(ComplianceActor, Persona(UserTypes.Staff, authenticated: false)));
    }
}
