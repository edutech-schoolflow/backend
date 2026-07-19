using System.IdentityModel.Tokens.Jwt;
using EduTech.Shared.Auth;
using EduTech.Shared.Constants;

namespace EduTech.Auth.Tests.Authentication;

/// <summary>
/// The JWT claim contract — the guardrail behind the EDD-012 JWT-slimming sprint (B2c). It decodes the
/// tokens the vendors actually mint and pins the exact claim set, so every claim that appears or leaves
/// the token is a deliberate, reviewed change. B2c.1 added the canonical identity (membership_id) +
/// organization_id; B2c.2 removes the dead flags/actor baggage — each edit here mirrors that contract
/// diff. Uses the pure TokenVendor (no DB), decoding with the standard JWT handler.
/// </summary>
public class JwtContractTests
{
    // HmacSha512 needs a >= 64-byte key.
    private const string Key = "test-signing-key-that-is-at-least-sixty-four-bytes-long-for-hmac512!!";
    private const string Iss = "EduTech";
    private const string Aud = "EduTechApp";

    private static readonly Guid Identity = Guid.NewGuid();
    private static readonly Guid Context = Guid.NewGuid();
    private static readonly Guid Membership = Guid.NewGuid();
    private static readonly Guid Organization = Guid.NewGuid();

    private static IReadOnlyDictionary<string, string> Claims(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.First().Value);

    [Fact]
    public void StaffScopedToken_CarriesCanonicalIdentity()
    {
        var features = new Dictionary<string, bool> { ["can_manage_students"] = true, ["can_view_fees"] = false };
        string token = TokenVendor.VendStaffScopedToken(Key, Iss, Aud,
            userId: Guid.NewGuid().ToString(), phone: "+2348010000001", schoolId: Organization.ToString(),
            affiliationId: Guid.NewGuid().ToString(), role: StaffRoles.Principal, employmentType: "full_time",
            kycStatus: "approved", features: features,
            identityId: Identity.ToString(), contextId: Context.ToString(),
            membershipId: Membership.ToString(), organizationId: Organization.ToString());

        var claims = Claims(token);

        // Canonical identity + context — the B2c.1 additions.
        Assert.Equal(Identity.ToString(), claims["identity_id"]);
        Assert.Equal(Context.ToString(), claims["context_id"]);
        Assert.Equal(Membership.ToString(), claims["membership_id"]);
        Assert.Equal(Organization.ToString(), claims["organization_id"]);
        Assert.Equal(UserTypes.Staff, claims["user_type"]);
        Assert.Equal(StaffRoles.Principal, claims["role"]);
        Assert.Equal(Organization.ToString(), claims["school_id"]);   // tenant binding (kept until FK-repoint)

        // B2c.2: authorization has LEFT the token — no permission flags, even when resolved features
        // are passed to the vendor. Enforcement is server-side (context_id → CapabilityResolver).
        Assert.False(claims.ContainsKey("can_manage_students"));
        Assert.False(claims.ContainsKey("can_view_fees"));

        // Load-bearing claims the retirement proof shows are still consumed — deliberately KEPT:
        // is_owner (13 business consumers) and affiliation_id (staff-action scoping).
        Assert.True(claims.ContainsKey("is_owner"));
        Assert.True(claims.ContainsKey("affiliation_id"));
    }

    [Fact]
    public void SchoolOwnerToken_CarriesCanonicalIdentity()
    {
        string token = TokenVendor.VendSchoolOwnerToken(Key, Iss, Aud,
            userId: Guid.NewGuid().ToString(), schoolId: Organization.ToString(), phone: "+2348010000002",
            schoolStatus: "active", kycStatus: "approved", subdomain: "divine",
            identityId: Identity.ToString(), contextId: Context.ToString(),
            membershipId: Membership.ToString(), organizationId: Organization.ToString());

        var claims = Claims(token);

        Assert.Equal(Membership.ToString(), claims["membership_id"]);
        Assert.Equal(Organization.ToString(), claims["organization_id"]);
        Assert.Equal(UserTypes.School, claims["user_type"]);
        Assert.Equal("true", claims["is_owner"]);
        Assert.Equal("owner", claims["role"]);
        Assert.True(claims.ContainsKey("subdomain"));      // B2c.2 removes
        Assert.True(claims.ContainsKey("school_status"));  // B2c.2 removes
    }

    [Fact]
    public void ParentToken_CarriesCanonicalIdentity()
    {
        string token = TokenVendor.VendParentToken(Key, Iss, Aud,
            userId: Guid.NewGuid().ToString(), phone: "+2348010000003",
            identityId: Identity.ToString(), contextId: Context.ToString(), schoolId: Organization.ToString(),
            membershipId: Membership.ToString(), organizationId: Organization.ToString());

        var claims = Claims(token);

        Assert.Equal(Membership.ToString(), claims["membership_id"]);
        Assert.Equal(Organization.ToString(), claims["organization_id"]);
        Assert.Equal(UserTypes.Parent, claims["user_type"]);
        Assert.Equal(Organization.ToString(), claims["school_id"]);   // org-scoped parent (EDD-002)
    }

    [Fact]
    public void ContextTokens_OmitCanonicalClaims_WhenNotProvided()
    {
        // Additive contract: the legacy/refresh paths that don't yet supply membership_id simply omit it
        // (nothing reads it yet). Proven so a missing claim is never a hard failure.
        string token = TokenVendor.VendParentToken(Key, Iss, Aud,
            userId: Guid.NewGuid().ToString(), phone: "+2348010000004");
        var claims = Claims(token);

        Assert.False(claims.ContainsKey("membership_id"));
        Assert.False(claims.ContainsKey("organization_id"));
        Assert.Equal(UserTypes.Parent, claims["user_type"]);   // the token is still valid + usable
    }
}
