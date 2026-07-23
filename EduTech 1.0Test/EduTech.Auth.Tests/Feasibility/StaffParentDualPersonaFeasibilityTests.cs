using System.IdentityModel.Tokens.Jwt;
using System.Text;
using EduTech.Auth.Staff;
using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using EduTech.Workforce;

namespace EduTech.Auth.Tests.Feasibility;

/// <summary>
/// FEASIBILITY SPIKE — NOT a feature we ship today.
///
/// Question: can one person be BOTH a staff member at the school they work at AND a parent of
/// children at a different school, and switch between the two contexts WITHOUT logging out?
///
/// These tests drive the real token machinery (<see cref="AccessTokenIssuer"/> → TokenVendor) to
/// map out what the current design already supports and what is structurally missing. Read the
/// asserts as findings, not as guarantees of behaviour.
///
/// VERDICT (see the individual tests):
///  • The TOKEN layer can already represent the dual persona — two tokens for the same phone, one
///    school-scoped (staff) and one school-agnostic (parent).
///  • What's MISSING for an in-place switch: a unified principal linking staff_users ↔ parents
///    (today they are unrelated rows with different GUIDs, joined only by phone), plus a
///    persona-switch endpoint that mints the other token from an already-authenticated session —
///    the staff school-switch (StaffSchoolService) is the pattern to copy. Without that, "switch"
///    means a fresh login as the other actor: different user_id, different signing key, and the
///    refresh family is actor-locked.
/// </summary>
public class StaffParentDualPersonaFeasibilityTests
{
    // HMAC-SHA512 keys must be >= 64 bytes. Distinct per portal, exactly as production wires them.
    private const string SchoolKey = "school-signing-key__school-signing-key__school-signing-key__0123456789";
    private const string StaffKey = "staff-signing-key___staff-signing-key___staff-signing-key___0123456789";
    private const string ParentKey = "parent-signing-key__parent-signing-key__parent-signing-key__0123456789";
    private const string AdminKey = "admin-signing-key___admin-signing-key___admin-signing-key___0123456789";

    private const string SharedPhone = "+2348137729210"; // the one thing the two identities share

    private static AccessTokenIssuer CreateIssuer()
    {
        Mock<IConfiguration> config = new();
        config.Setup(c => c["Jwt:SchoolSigningKey"]).Returns(SchoolKey);
        config.Setup(c => c["Jwt:StaffSigningKey"]).Returns(StaffKey);
        config.Setup(c => c["Jwt:ParentSigningKey"]).Returns(ParentKey);
        config.Setup(c => c["Jwt:PlatformAdminSigningKey"]).Returns(AdminKey);
        config.Setup(c => c["Jwt:Issuer"]).Returns("EduTech");
        config.Setup(c => c["Jwt:Audience"]).Returns("EduTechApp");
        return new AccessTokenIssuer(config.Object);
    }

    private static JwtSecurityToken Decode(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    private static string? ClaimValue(JwtSecurityToken jwt, string type) =>
        jwt.Claims.FirstOrDefault(c => c.Type == type)?.Value;

    // ── FINDING 1: the dual persona is representable today ─────────────────────
    // Staff-at-his-school (scoped, school_id = A) and parent-of-other-kids (school-agnostic) can
    // both be minted for the SAME phone. The parent token has no school_id, so it naturally covers
    // a child at a DIFFERENT school (school B) — access is per-request via parent→child links.
    [Fact]
    public void SamePhone_CanHold_StaffScopedTokenForHisSchool_AndSchoolAgnosticParentToken()
    {
        AccessTokenIssuer issuer = CreateIssuer();

        Guid staffUserId = Guid.NewGuid();
        Guid schoolAWhereHeWorks = Guid.NewGuid();
        Guid affiliationId = Guid.NewGuid();
        IReadOnlyDictionary<string, bool> features =
            StaffFeatureResolver.Resolve(StaffRoles.Teacher, null, null);

        Guid parentId = Guid.NewGuid();

        JwtSecurityToken staff = Decode(issuer.IssueStaffScoped(
            staffUserId, schoolAWhereHeWorks, affiliationId, SharedPhone,
            StaffRoles.Teacher, EmploymentTypes.PartTime, "verified", features).Token);

        JwtSecurityToken parent = Decode(issuer.IssueParent(parentId, SharedPhone).Token);

        // Same human — same phone on both tokens.
        Assert.Equal(SharedPhone, ClaimValue(staff, "phone"));
        Assert.Equal(SharedPhone, ClaimValue(parent, "phone"));

        // Staff token is bound to the school he works at...
        Assert.Equal(schoolAWhereHeWorks.ToString(), ClaimValue(staff, "school_id"));
        Assert.Equal(UserTypes.Staff, ClaimValue(staff, "user_type"));

        // ...the parent token is school-AGNOSTIC (no school_id), so it can cover children anywhere,
        // including a different school. This half of the scenario already works.
        Assert.Null(ClaimValue(parent, "school_id"));
        Assert.Equal(UserTypes.Parent, ClaimValue(parent, "user_type"));
    }

    // ── FINDING 2: the structural blocker — no unified principal ───────────────
    // The two tokens carry DIFFERENT subjects (staff_users.id vs parents.id). Nothing links them
    // but the phone string, so the backend cannot treat them as one principal with two personas.
    [Fact]
    public void StaffAndParentTokens_HaveDifferentSubjectIds_SoThereIsNoUnifiedPrincipal()
    {
        AccessTokenIssuer issuer = CreateIssuer();

        Guid staffUserId = Guid.NewGuid();
        Guid parentId = Guid.NewGuid();

        JwtSecurityToken staff = Decode(
            issuer.IssueStaffIdentity(staffUserId, SharedPhone, "verified").Token);
        JwtSecurityToken parent = Decode(issuer.IssueParent(parentId, SharedPhone).Token);

        string? staffUserClaim = ClaimValue(staff, "user_id");
        string? parentUserClaim = ClaimValue(parent, "user_id");

        Assert.Equal(staffUserId.ToString(), staffUserClaim);
        Assert.Equal(parentId.ToString(), parentUserClaim);

        // The crux: different ids, different user_type. A switch needs a person id that owns both.
        Assert.NotEqual(staffUserClaim, parentUserClaim);
        Assert.NotEqual(ClaimValue(staff, "user_type"), ClaimValue(parent, "user_type"));
    }

    // ── FINDING 3: one credential cannot serve both contexts ───────────────────
    // Staff and parent tokens are signed with DIFFERENT keys (separate portals/schemes). A single
    // sf_access cookie validated by the staff scheme will reject a parent token and vice-versa —
    // so "switching" cannot be done by reinterpreting the token you already hold.
    [Fact]
    public void StaffToken_IsRejected_ByTheParentSigningKey()
    {
        AccessTokenIssuer issuer = CreateIssuer();
        string staffToken = issuer.IssueStaffIdentity(Guid.NewGuid(), SharedPhone, "verified").Token;

        JwtSecurityTokenHandler handler = new();

        // Validates fine under its OWN key...
        handler.ValidateToken(staffToken, ValidationParams(StaffKey), out _);

        // ...but the parent scheme (different key) rejects it.
        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(staffToken, ValidationParams(ParentKey), out _));
    }

    // ── THE MISSING CAPABILITY (documented, intentionally skipped) ─────────────
    // What we'd build to make the scenario real:
    //   1. A `people` (principal) table keyed by normalized phone; staff_users + parents FK to it.
    //   2. On login, resolve ALL personas for that principal (staff affiliations + parent profile).
    //   3. A POST /auth/switch-persona endpoint that, from an authenticated session, mints the other
    //      persona's token — mirroring StaffSchoolService.SwitchAsync (which already switches between
    //      staff affiliations and re-issues a scoped token without re-login).
    // Un-skip and implement against that design when we decide to ship it.
    /// <summary>
    /// FORMERLY SKIPPED — the two missing pieces now exist (EDD-001): the unified principal is the
    /// <c>identities</c> table, and the persona switch is the unified login's context selection.
    /// One identity holding a STAFF employment and a PARENT relationship gets BOTH contexts from one
    /// login, and entering either mints the matching portal token — no second account, no re-login
    /// as a different person.
    /// </summary>
    [Fact]
    public async Task Person_CanSwitch_StaffToParent_WithoutReLogin()
    {
        var identities = new Mock<EduTech.Identity.IIdentityRepository>();
        var contexts = new Mock<EduTech.Auth.Unified.IAuthContextRepository>();
        var parents = new Mock<EduTech.Auth.Parent.IParentRepository>();
        var hasher = new Mock<EduTech.Auth.Security.IPasswordHasher>();
        var otp = new Mock<EduTech.Auth.Otp.IOtpService>();
        var sms = new Mock<EduTech.Shared.Notifications.INotificationDispatcher>();
        var access = new Mock<IAccessTokenIssuer>();
        var refresh = new Mock<EduTech.Auth.RefreshTokens.IRefreshTokenService>();
        var staffUsers = new Mock<IStaffUserRepository>();
        var affiliations = new Mock<IStaffAffiliationRepository>();
        var templates = new Mock<IPermissionTemplateRepository>();
        var overrides = new Mock<IStaffFeatureOverrideRepository>();

        var identity = new EduTech.Identity.Domain.Identity(Guid.NewGuid(), "Dual", null, "Persona",
            SharedPhone, null, "hashed", true, false, EduTech.Identity.Domain.IdentityStatus.Active, 0, null);
        identities.Setup(i => i.GetByPhoneAsync(SharedPhone, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        hasher.Setup(h => h.Verify("password123", "hashed")).Returns(true);

        Guid parentId = Guid.NewGuid();
        Guid affiliationId = Guid.NewGuid();
        Guid staffUserId = Guid.NewGuid();
        Guid schoolId = Guid.NewGuid();
        contexts.Setup(c => c.ListAccessContextsAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EduTech.Auth.Unified.AccessContextRow
                    { ReferenceId = affiliationId, Type = "staff", OrganizationId = schoolId, OrganizationName = "Divine Wisdom", Role = "teacher" },
                new EduTech.Auth.Unified.AccessContextRow { ReferenceId = parentId, Type = "parent" }
            });
        contexts.Setup(c => c.ListStaffContextsAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new EduTech.Auth.Unified.StaffContextRow
                { StaffUserId = staffUserId, AffiliationId = affiliationId, SchoolId = schoolId, SchoolName = "Divine Wisdom", Role = "teacher" } });

        affiliations.Setup(a => a.GetActiveForSwitchAsync(staffUserId, schoolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffSwitchRow { AffiliationId = affiliationId, Role = "teacher", EmploymentType = "full_time" });
        staffUsers.Setup(su => su.GetTokenClaimsAsync(staffUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffUserTokenRow { Phone = SharedPhone, KycStatus = "approved" });
        overrides.Setup(o => o.GetForAffiliationAsync(affiliationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, bool>());
        access.Setup(a => a.IssueStaffScoped(staffUserId, schoolId, affiliationId, SharedPhone, "teacher",
                "full_time", "approved", It.IsAny<IReadOnlyDictionary<string, bool>>(),
                It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>()))
            .Returns(new AccessToken { Token = "staff-token", ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        access.Setup(a => a.IssueParent(parentId, SharedPhone, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<Guid?>(), It.IsAny<Guid?>()))
            .Returns(new AccessToken { Token = "parent-token", ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        refresh.Setup(r => r.IssueAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EduTech.Auth.RefreshTokens.RefreshTokenIssue
                { Token = "r", FamilyId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddHours(12) });

        var sut = new EduTech.Auth.Unified.UnifiedAuthService(identities.Object, contexts.Object,
            parents.Object, hasher.Object, otp.Object, sms.Object, access.Object, refresh.Object,
            staffUsers.Object, affiliations.Object, templates.Object, overrides.Object,
            new Mock<EduTech.Auth.SchoolOwner.ISchoolRepository>().Object,
            new Mock<EduTech.Auth.SchoolOwner.ISchoolOwnerRepository>().Object,
            new Mock<EduTech.Membership.IMembershipRepository>().Object,
            new Mock<EduTech.People.IEmploymentRepository>().Object,
            new Mock<EduTech.Auth.Unified.IAccessContextProjector>().Object,
            new Mock<EduTech.Shared.Persistence.IDbConnectionFactory>().Object);

        access.Setup(a => a.IssueIdentity(identity.Id, SharedPhone))
            .Returns(new AccessToken { Token = "identity-token", ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        contexts.Setup(c => c.GetIdentityIdForActorAsync("identity", identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity.Id);
        identities.Setup(i => i.GetByIdAsync(identity.Id, It.IsAny<CancellationToken>())).ReturnsAsync(identity);

        // ONE login → BOTH personas offered (the unified principal) + an identity session for the picker.
        var offered = await sut.LoginAsync(
            new EduTech.Auth.Unified.UnifiedLoginRequest { Phone = SharedPhone, Password = "password123" },
            null, null, CancellationToken.None);
        Assert.Equal(2, offered.Contexts.Count);
        Assert.Null(offered.Selected);

        // Enter STAFF, then switch to PARENT — one session, /select-context, different portal tokens.
        var asStaff = await sut.SelectContextAsync("identity", identity.Id, affiliationId,
            null, null, CancellationToken.None);
        Assert.Equal("staff-token", asStaff.Tokens!.AccessToken);

        var asParent = await sut.SelectContextAsync("identity", identity.Id, parentId,
            null, null, CancellationToken.None);
        Assert.Equal("parent-token", asParent.Tokens!.AccessToken);
    }

    private static TokenValidationParameters ValidationParams(string key) => new()
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = false,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
    };
}
