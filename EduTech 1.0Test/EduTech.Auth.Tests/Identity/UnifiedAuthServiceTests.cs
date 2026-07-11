using EduTech.Auth.Otp;
using EduTech.Auth.Parent;
using EduTech.Auth.RefreshTokens;
using EduTech.Auth.Security;
using EduTech.Auth.Staff;
using EduTech.Auth.Tokens;
using EduTech.Auth.Unified;
using EduTech.Identity;
using EduTech.Identity.Domain;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using Moq;
using IdentityAggregate = EduTech.Identity.Domain.Identity;
using EduTech.Workforce;

namespace EduTech.Auth.Tests.Identity;

/// <summary>
/// EDD-001 Sprint 2 — one register (identity + transition dual-write) and one login
/// (contexts decide the portal; auto-enter on a single context).
/// </summary>
public class UnifiedAuthServiceTests
{
    private readonly Mock<IIdentityRepository> _identities = new();
    private readonly Mock<IAuthContextRepository> _contexts = new();
    private readonly Mock<IParentRepository> _parents = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IOtpService> _otp = new();
    private readonly Mock<INotificationDispatcher> _sms = new();
    private readonly Mock<IAccessTokenIssuer> _access = new();
    private readonly Mock<IRefreshTokenService> _refresh = new();
    private readonly Mock<IStaffUserRepository> _staffUsers = new();
    private readonly Mock<IStaffAffiliationRepository> _affiliations = new();
    private readonly Mock<IPermissionTemplateRepository> _templates = new();
    private readonly Mock<IStaffFeatureOverrideRepository> _overrides = new();
    private readonly Mock<EduTech.Auth.SchoolOwner.ISchoolRepository> _schools = new();
    private readonly Mock<EduTech.Auth.SchoolOwner.ISchoolOwnerRepository> _owners = new();
    private readonly Mock<EduTech.Shared.Persistence.IDbConnectionFactory> _dbFactory = new();

    private UnifiedAuthService CreateSut() => new(
        _identities.Object, _contexts.Object, _parents.Object, _hasher.Object, _otp.Object,
        _sms.Object, _access.Object, _refresh.Object, _staffUsers.Object, _affiliations.Object,
        _templates.Object, _overrides.Object, _schools.Object, _owners.Object, _dbFactory.Object);

    private const string Phone = "+2348033334444";

    private static IdentityAggregate Identity(Guid? id = null, string? passwordHash = null,
        bool phoneVerified = false, IdentityStatus status = IdentityStatus.Pending)
        => new IdentityAggregate(id ?? Guid.NewGuid(), "Ada", null, "Obi", Phone, null,
            passwordHash, phoneVerified, false, status, 0, null);

    private void NoContexts(Guid identityId)
    {
        _contexts.Setup(c => c.ListOwnerContextsAsync(identityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OwnerContextRow>());
        _contexts.Setup(c => c.ListStaffContextsAsync(identityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StaffContextRow>());
        _contexts.Setup(c => c.GetParentContextAsync(identityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParentContextRow?)null);
        _contexts.Setup(c => c.ListAccessContextsAsync(identityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccessContextRow>());
    }

    // ── register ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_NewPhone_CreatesIdentityONLY_SendsOtp()
    {
        // Identity-first (EDD-001): registration creates an identity and NOTHING else — no parent
        // profile, no context. Relationships create those later.
        Guid identityId = Guid.NewGuid();
        _hasher.Setup(h => h.Hash("password123")).Returns("hashed");
        _identities.Setup(i => i.GetByPhoneAsync(Phone, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityAggregate?)null);
        _identities.Setup(i => i.CreateAsync("Ada", null, "Obi", Phone, null, "hashed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(identityId);
        _parents.Setup(p => p.GetIdByPhoneAsync(Phone, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        _otp.Setup(o => o.GenerateAsync(OtpPurpose.IdentityPhoneVerification, identityId, Phone,
            It.IsAny<CancellationToken>())).ReturnsAsync("123456");

        await CreateSut().RegisterAsync(new UnifiedRegisterRequest
        {
            FirstName = "Ada", LastName = "Obi", Phone = "08033334444", Password = "password123"
        }, CancellationToken.None);

        _parents.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _contexts.Verify(c => c.LinkParentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _sms.Verify(s => s.SendSmsAsync(Phone, It.Is<string>(m => m.Contains("123456")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_SchoolSeededGuardian_ClaimsIdentityAndLinksExistingProfile()
    {
        Guid identityId = Guid.NewGuid();
        Guid parentId = Guid.NewGuid();
        _hasher.Setup(h => h.Hash("password123")).Returns("hashed");
        _identities.Setup(i => i.GetByPhoneAsync(Phone, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Identity(identityId)); // pending, no password
        _identities.Setup(i => i.ClaimAsync(identityId, "Ada", null, "Obi", null, "hashed",
            It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _parents.Setup(p => p.GetIdByPhoneAsync(Phone, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentId); // the school-created guardian profile
        _otp.Setup(o => o.GenerateAsync(OtpPurpose.IdentityPhoneVerification, identityId, Phone,
            It.IsAny<CancellationToken>())).ReturnsAsync("111111");

        await CreateSut().RegisterAsync(new UnifiedRegisterRequest
        {
            FirstName = "Ada", LastName = "Obi", Phone = "08033334444", Password = "password123"
        }, CancellationToken.None);

        _identities.Verify(i => i.ClaimAsync(identityId, "Ada", null, "Obi", null, "hashed",
            It.IsAny<CancellationToken>()), Times.Once);
        _identities.Verify(i => i.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        // the pre-existing guardian profile is linked (relationship already exists), never re-created
        _contexts.Verify(c => c.LinkParentAsync(parentId, identityId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_NoContexts_IssuesIdentityScopeSession()
    {
        // Fresh identity, zero relationships → contexts [] and an IDENTITY session for the hub.
        IdentityAggregate identity = Identity(passwordHash: "hashed", phoneVerified: true, status: IdentityStatus.Active);
        _identities.Setup(i => i.GetByPhoneAsync(Phone, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        _hasher.Setup(h => h.Verify("password123", "hashed")).Returns(true);
        NoContexts(identity.Id);
        _access.Setup(a => a.IssueIdentity(identity.Id, Phone))
            .Returns(new AccessToken { Token = "identity-access", ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        _refresh.Setup(r => r.IssueAsync("identity", identity.Id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenIssue { Token = "r", FamilyId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddHours(12) });

        UnifiedLoginResult result = await CreateSut().LoginAsync(
            new UnifiedLoginRequest { Phone = "08033334444", Password = "password123" }, null, null, CancellationToken.None);

        Assert.Empty(result.Contexts);
        Assert.Null(result.Selected);
        Assert.Equal("identity-access", result.Tokens!.AccessToken);
    }

    [Fact]
    public async Task Register_ClaimedIdentity_Throws409()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        _identities.Setup(i => i.GetByPhoneAsync(Phone, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Identity(passwordHash: "already"));

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().RegisterAsync(
            new UnifiedRegisterRequest { FirstName = "X", LastName = "Y", Phone = "08033334444", Password = "password123" },
            CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    // ── login ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WrongPassword_RecordsFailure_Throws401()
    {
        IdentityAggregate identity = Identity(passwordHash: "hashed", phoneVerified: true, status: IdentityStatus.Active);
        _identities.Setup(i => i.GetByPhoneAsync(Phone, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        _hasher.Setup(h => h.Verify("wrong", "hashed")).Returns(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().LoginAsync(
            new UnifiedLoginRequest { Phone = "08033334444", Password = "wrong" }, null, null, CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
        _identities.Verify(i => i.SaveLoginStateAsync(identity.Id, 1, null, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Login_SingleParentContext_AutoEnters_WithParentTokens()
    {
        Guid parentId = Guid.NewGuid();
        IdentityAggregate identity = Identity(passwordHash: "hashed", phoneVerified: true, status: IdentityStatus.Active);
        _identities.Setup(i => i.GetByPhoneAsync(Phone, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        _hasher.Setup(h => h.Verify("password123", "hashed")).Returns(true);
        NoContexts(identity.Id);
        _contexts.Setup(c => c.ListAccessContextsAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AccessContextRow { ReferenceId = parentId, Type = "parent" } });
        _access.Setup(a => a.IssueParent(parentId, Phone, It.IsAny<Guid?>(), It.IsAny<Guid?>()))
            .Returns(new AccessToken { Token = "access", ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        _refresh.Setup(r => r.IssueAsync(AuthActorTypes.Parent, parentId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenIssue { Token = "refresh", FamilyId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddDays(14) });

        UnifiedLoginResult result = await CreateSut().LoginAsync(
            new UnifiedLoginRequest { Phone = "08033334444", Password = "password123" }, null, null, CancellationToken.None);

        Assert.Equal(parentId, result.Selected);
        Assert.NotNull(result.Tokens);
        Assert.Equal("access", result.Tokens!.AccessToken);
    }

    [Fact]
    public async Task Login_MultipleContexts_ListsThemWithIdentitySession()
    {
        Guid parentId = Guid.NewGuid();
        Guid ownerId = Guid.NewGuid();
        IdentityAggregate identity = Identity(passwordHash: "hashed", phoneVerified: true, status: IdentityStatus.Active);
        _identities.Setup(i => i.GetByPhoneAsync(Phone, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        _hasher.Setup(h => h.Verify("password123", "hashed")).Returns(true);
        _contexts.Setup(c => c.ListAccessContextsAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AccessContextRow { ReferenceId = ownerId, Type = "owner", OrganizationId = Guid.NewGuid(), OrganizationName = "Divine Wisdom" },
                new AccessContextRow { ReferenceId = parentId, Type = "parent" }
            });

        _access.Setup(a => a.IssueIdentity(identity.Id, Phone))
            .Returns(new AccessToken { Token = "identity-access", ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        _refresh.Setup(r => r.IssueAsync("identity", identity.Id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenIssue { Token = "r", FamilyId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddHours(12) });

        UnifiedLoginResult result = await CreateSut().LoginAsync(
            new UnifiedLoginRequest { Phone = "08033334444", Password = "password123" }, null, null, CancellationToken.None);

        // No auto-enter — but the person IS authenticated: identity session for the picker.
        Assert.Null(result.Selected);
        Assert.Equal("identity-access", result.Tokens!.AccessToken);
        Assert.Equal(2, result.Contexts.Count);
    }

    [Fact]
    public async Task SelectContext_NotOwned_Throws403()
    {
        IdentityAggregate identity = Identity(passwordHash: "hashed", phoneVerified: true, status: IdentityStatus.Active);
        _contexts.Setup(c => c.GetIdentityIdForActorAsync("identity", identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity.Id);
        _identities.Setup(i => i.GetByIdAsync(identity.Id, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        NoContexts(identity.Id);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().SelectContextAsync(
            "identity", identity.Id, Guid.NewGuid(), null, null, CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Login_OwnerContext_IssuesSchoolOwnerToken()
    {
        Guid ownerId = Guid.NewGuid();
        Guid schoolId = Guid.NewGuid();
        IdentityAggregate identity = Identity(passwordHash: "hashed", phoneVerified: true, status: IdentityStatus.Active);
        _identities.Setup(i => i.GetByPhoneAsync(Phone, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        _hasher.Setup(h => h.Verify("password123", "hashed")).Returns(true);
        _contexts.Setup(c => c.ListAccessContextsAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AccessContextRow
                { ReferenceId = ownerId, Type = "owner", OrganizationId = schoolId, OrganizationName = "Divine Wisdom" } });
        _contexts.Setup(c => c.ListOwnerContextsAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new OwnerContextRow
            {
                OwnerId = ownerId, SchoolId = schoolId, SchoolName = "Divine Wisdom",
                Status = "active", KycStatus = "approved", Subdomain = "divine"
            } });
        _access.Setup(a => a.IssueSchoolOwner(ownerId, schoolId, Phone, "active", "approved", "divine",
                It.IsAny<Guid?>(), It.IsAny<Guid?>()))
            .Returns(new AccessToken { Token = "owner-access", ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        _refresh.Setup(r => r.IssueAsync(AuthActorTypes.SchoolOwner, ownerId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenIssue { Token = "r", FamilyId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddHours(12) });

        UnifiedLoginResult result = await CreateSut().LoginAsync(
            new UnifiedLoginRequest { Phone = "08033334444", Password = "password123" }, null, null, CancellationToken.None);

        Assert.Equal(ownerId, result.Selected);
        Assert.Equal("owner-access", result.Tokens!.AccessToken);
    }

    // ── Organization Wizard: setup (name + re-slug), owner-only ─────────────────────

    private void OrgWorkspace(Guid identityId, string slug, string type)
    {
        // The org resolved at its CURRENT (placeholder) slug — name still null.
        _contexts.Setup(c => c.GetOrganizationBySlugAsync(slug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationRow
            {
                Id = OrgId, Slug = slug, Name = null, Status = "pending_kyc", KycStatus = "not_submitted"
            });
        _contexts.Setup(c => c.ListAccessContextsAsync(identityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AccessContextRow
            {
                ReferenceId = Guid.NewGuid(), Type = type, OrganizationId = OrgId, OrganizationSlug = slug
            } });
    }

    private void OrgResolvesAt(string newSlug, string name)
    {
        // The re-resolve after renaming — org now carries the name + new slug.
        _contexts.Setup(c => c.GetOrganizationBySlugAsync(newSlug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationRow
            {
                Id = OrgId, Slug = newSlug, Name = name, Status = "pending_kyc", KycStatus = "not_submitted"
            });
    }

    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public async Task Setup_Owner_NamesAndReslugsFromName()
    {
        Guid ident = Guid.NewGuid();
        OrgWorkspace(ident, "s-0a1b2c3d", "owner");
        OrgResolvesAt("divine-wisdom", "Divine Wisdom");
        _contexts.Setup(c => c.SlugTakenAsync("divine-wisdom", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        OrganizationWorkspaceResponse res = await CreateSut().SetupOrganizationAsync(
            ident, "s-0a1b2c3d", new SetupOrganizationRequest { Name = " Divine Wisdom ", Type = "Primary" },
            CancellationToken.None);

        Assert.Equal("divine-wisdom", res.Slug);
        Assert.Equal("Divine Wisdom", res.Name);
        _contexts.Verify(c => c.SetOrganizationDetailsAsync(
            OrgId, "Divine Wisdom", "primary", null, "divine-wisdom", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Setup_SlugTaken_SuffixesUntilFree()
    {
        Guid ident = Guid.NewGuid();
        OrgWorkspace(ident, "s-0a1b2c3d", "owner");
        OrgResolvesAt("divine-wisdom-2", "Divine Wisdom");
        _contexts.Setup(c => c.SlugTakenAsync("divine-wisdom", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _contexts.Setup(c => c.SlugTakenAsync("divine-wisdom-2", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        OrganizationWorkspaceResponse res = await CreateSut().SetupOrganizationAsync(
            ident, "s-0a1b2c3d", new SetupOrganizationRequest { Name = "Divine Wisdom" }, CancellationToken.None);

        Assert.Equal("divine-wisdom-2", res.Slug);
        _contexts.Verify(c => c.SetOrganizationDetailsAsync(
            OrgId, "Divine Wisdom", null, null, "divine-wisdom-2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Setup_NonOwnerContext_Throws403()
    {
        Guid ident = Guid.NewGuid();
        OrgWorkspace(ident, "s-0a1b2c3d", "staff");   // a staff member in the same org

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SetupOrganizationAsync(ident, "s-0a1b2c3d",
                new SetupOrganizationRequest { Name = "Divine Wisdom" }, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        _contexts.Verify(c => c.SetOrganizationDetailsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Setup_EmptyName_Throws400()
    {
        Guid ident = Guid.NewGuid();
        OrgWorkspace(ident, "s-0a1b2c3d", "owner");

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SetupOrganizationAsync(ident, "s-0a1b2c3d",
                new SetupOrganizationRequest { Name = "   " }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Setup_UnknownSlugOrNotMine_Throws404()
    {
        Guid ident = Guid.NewGuid();
        _contexts.Setup(c => c.GetOrganizationBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationRow?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SetupOrganizationAsync(ident, "s-unknown",
                new SetupOrganizationRequest { Name = "Divine Wisdom" }, CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }
}
