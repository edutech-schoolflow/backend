using System.IdentityModel.Tokens.Jwt;
using System.Text;
using EduTech.Auth.Tokens;
using EduTech.Shared.Auth;
using EduTech.Shared.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;

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
        string token = TokenVendor.VendStaffScopedToken(Key, Iss, Aud,
            userId: Guid.NewGuid().ToString(), phone: "+2348010000001", schoolId: Organization.ToString(),
            affiliationId: Guid.NewGuid().ToString(), role: StaffRoles.Principal, employmentType: "full_time",
            kycStatus: "approved",
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

        // Authorization has LEFT the token (B2c.2) — no permission flags; enforcement is server-side
        // (context_id → CapabilityResolver).
        Assert.False(claims.ContainsKey("can_manage_students"));
        Assert.False(claims.ContainsKey("can_view_fees"));

        // B2c.3d: the transitional claims are gone (0 readers).
        Assert.False(claims.ContainsKey("employment_type"));
        Assert.False(claims.ContainsKey("kyc_status"));
        Assert.False(claims.ContainsKey("active_school_id"));

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
        // B2c.3d: the transitional owner claims are retired (0 readers).
        Assert.False(claims.ContainsKey("subdomain"));
        Assert.False(claims.ContainsKey("school_status"));
        Assert.False(claims.ContainsKey("kyc_status"));
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

    // HmacSha512 key #2 (>= 64 bytes) — the isolated platform-admin trust boundary.
    private const string AdminKey = "test-admin-signing-key-that-is-also-at-least-sixty-four-bytes-long!!!!";

    /// <summary>
    /// EDD-012 B2c.3a: the real <see cref="AccessTokenIssuer"/>, configured with ONLY the unified
    /// <c>Jwt:SigningKey</c> (no per-portal keys), mints every identity/portal token under that one key —
    /// so a single validation key accepts them all. Platform-admin stays on its own key (a leak of the
    /// user key can never forge an admin token).
    /// </summary>
    [Fact]
    public void OneSigningKey_ValidatesEveryUserToken_AdminStaysIsolated()
    {
        Mock<IConfiguration> config = new();
        config.Setup(c => c["Jwt:SigningKey"]).Returns(Key);
        config.Setup(c => c["Jwt:PlatformAdminSigningKey"]).Returns(AdminKey);
        config.Setup(c => c["Jwt:Issuer"]).Returns(Iss);
        config.Setup(c => c["Jwt:Audience"]).Returns(Aud);
        // Deliberately no Jwt:StaffSigningKey/SchoolSigningKey/... — proves the issuer needs only the one key.
        AccessTokenIssuer issuer = new(config.Object);

        string owner = issuer.IssueSchoolOwner(Guid.NewGuid(), Organization, "+2348010000001",
            "active", "approved", "divine", Identity, Context, Membership, Organization).Token;
        string staff = issuer.IssueStaffScoped(Guid.NewGuid(), Organization, Guid.NewGuid(), "+2348010000002",
            StaffRoles.Teacher, "full_time", "approved",
            Identity, Context, Membership, Organization).Token;
        string parent = issuer.IssueParent(Guid.NewGuid(), "+2348010000003", Identity, Context,
            Organization, Membership, Organization).Token;
        string identity = issuer.IssueIdentity(Identity, "+2348010000004").Token;
        string admin = issuer.IssuePlatformAdmin(Guid.NewGuid(), "super_admin", "a@b.com").Token;

        // Every user token validates under the ONE signing key.
        foreach (string token in new[] { owner, staff, parent, identity })
        {
            Assert.True(Validates(token, Key), "user token should validate with the unified key");
        }

        // Admin is isolated: it does NOT validate with the user key, only with its own.
        Assert.False(Validates(admin, Key));
        Assert.True(Validates(admin, AdminKey));
    }

    private static bool Validates(string token, string key)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };
        TokenValidationParameters pars = new()
        {
            ValidateIssuer = true, ValidIssuer = Iss,
            ValidateAudience = true, ValidAudience = Aud,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ClockSkew = TimeSpan.Zero
        };
        try { handler.ValidateToken(token, pars, out _); return true; }
        catch { return false; }
    }
}
