using EduTech.Auth.Otp;
using EduTech.Auth.Parent;
using EduTech.Auth.SchoolOwner;
using EduTech.Auth.RefreshTokens;
using EduTech.Auth.Security;
using EduTech.Auth.Staff;
using EduTech.Auth.Tokens;
using EduTech.Identity;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using EduTech.Shared.Persistence;
using EduTech.Shared.Phone;
using Npgsql;
using EduTech.Workforce;
using EduTech.Membership;
using EduTech.Membership.Domain;

namespace EduTech.Auth.Unified;

/// <summary>
/// EDD-001 Sprint 2 — the ONE registration and ONE login (phone + password, D1).
///
/// Register creates an Identity only (claiming a pending one when the phone was pre-created by a
/// school). During the transition it dual-writes the parent profile so the existing parent portal —
/// and its legacy login — keep working; Sprint 5 removes the legacy side.
///
/// Login authenticates the Identity (aggregate guards: lockout, suspension, verification) and then
/// loads the identity's CONTEXTS. Exactly one → auto-enter; several → the caller picks and logs in
/// again with the context key. Entering a context mints the matching LEGACY-SHAPED portal token, so
/// every existing policy/filter keeps working unchanged.
/// </summary>
public interface IUnifiedAuthService
{
    Task RegisterAsync(UnifiedRegisterRequest request, CancellationToken cancellationToken);
    Task VerifyPhoneAsync(UnifiedVerifyPhoneRequest request, CancellationToken cancellationToken);
    Task ResendOtpAsync(string phone, CancellationToken cancellationToken);

    Task<UnifiedLoginResult> LoginAsync(UnifiedLoginRequest request, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken);

    Task ForgotPasswordAsync(string phone, CancellationToken cancellationToken);
    Task ResetPasswordAsync(UnifiedResetPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>The signed-in person (any portal token) resolved to their identity + contexts. The
    /// current context id (from the token) rides along so the switcher can mark/exclude it.</summary>
    Task<UnifiedMeResponse> GetMeAsync(string userType, Guid actorId, Guid? currentContextId,
        CancellationToken cancellationToken);

    /// <summary>Same, when the token already carries identity_id (org-context tokens).</summary>
    Task<UnifiedMeResponse> GetMeByIdentityAsync(Guid identityId, Guid? currentContextId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enters one of the signed-in identity's contexts: validates ownership, mints the portal
    /// session. The ONLY way a session changes context (EDD-001 Q4: "which context have you chosen?").
    /// </summary>
    Task<UnifiedLoginResult> SelectContextAsync(string userType, Guid actorId, Guid contextId,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken);

    /// <summary>
    /// Organization onboarding (EDD-004 Workflow 1): an AUTHENTICATED, verified identity creates a
    /// school — shell + owner employment — and is handed the owner context. Registration and
    /// organization creation are separate acts; nobody registers "as a school".
    /// </summary>
    Task<UnifiedLoginResult> CreateOrganizationAsync(string userType, Guid actorId,
        SetupOrganizationRequest request, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken);

    /// <summary>Legacy-session bridge: portal actor → identity id (org-context tokens carry it directly).</summary>
    Task<Guid> ResolveIdentityIdAsync(string userType, Guid actorId, CancellationToken cancellationToken);

    /// <summary>
    /// EDD-005 Principle 6 — sessions are independent of routes. The ONE refresh for every session
    /// kind: rotates the presented refresh token and mints whatever access token its actor implies
    /// (identity-scope, parent, staff, or owner). The browser URL plays no part.
    /// </summary>
    Task<UnifiedTokens> RefreshSessionAsync(string refreshToken, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a workspace URL (FE-001 Phase 2, /o/{slug}) for the signed-in identity: the
    /// organization plus the caller's own context there — 404 unless they belong to it.
    /// </summary>
    Task<OrganizationWorkspaceResponse> GetOrganizationWorkspaceAsync(Guid identityId, string slug,
        CancellationToken cancellationToken);

    /// <summary>
    /// Organization Wizard: names a bootstrapped org (fixing the null name) and re-slugs from the
    /// name. Owner-only. Returns the workspace at its NEW slug so the caller re-routes.
    /// </summary>
    Task<OrganizationWorkspaceResponse> SetupOrganizationAsync(Guid identityId, string slug,
        SetupOrganizationRequest request, CancellationToken cancellationToken);

    /// <summary>What /welcome should offer this identity: pending invites + unfinished organizations.</summary>
    Task<WelcomeResponse> GetWelcomeAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>
    /// EDD-005 — the identity home: one call carrying identity, profiles, capabilities,
    /// organizations, invitations, drafts and family counts. The platform landing renders from it.
    /// </summary>
    Task<PlatformHomeProjection> GetPlatformHomeAsync(Guid identityId, Guid? currentContextId,
        CancellationToken cancellationToken);
}

internal sealed class UnifiedAuthService : IUnifiedAuthService
{
    private const int MinPasswordLength = 8;
    private const string RegistrationFailed = "Registration failed. Something went wrong.";
    private const string InvalidCredentials = "Invalid phone or password.";

    private readonly IIdentityRepository _identities;
    private readonly IAuthContextRepository _contexts;
    private readonly IParentRepository _parents;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IOtpService _otpService;
    private readonly INotificationDispatcher _notifications;
    private readonly IAccessTokenIssuer _accessTokenIssuer;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IStaffUserRepository _staffUsers;
    private readonly IStaffAffiliationRepository _affiliations;
    private readonly IPermissionTemplateRepository _permissionTemplates;
    private readonly IStaffFeatureOverrideRepository _overrides;
    private readonly ISchoolRepository _schools;
    private readonly ISchoolOwnerRepository _owners;
    private readonly IMembershipRepository _memberships;
    private readonly IDbConnectionFactory _connectionFactory;

    public UnifiedAuthService(
        IIdentityRepository identities,
        IAuthContextRepository contexts,
        IParentRepository parents,
        IPasswordHasher passwordHasher,
        IOtpService otpService,
        INotificationDispatcher notifications,
        IAccessTokenIssuer accessTokenIssuer,
        IRefreshTokenService refreshTokens,
        IStaffUserRepository staffUsers,
        IStaffAffiliationRepository affiliations,
        IPermissionTemplateRepository permissionTemplates,
        IStaffFeatureOverrideRepository overrides,
        ISchoolRepository schools,
        ISchoolOwnerRepository owners,
        IMembershipRepository memberships,
        IDbConnectionFactory connectionFactory)
    {
        _schools = schools;
        _owners = owners;
        _memberships = memberships;
        _connectionFactory = connectionFactory;
        _identities = identities;
        _contexts = contexts;
        _parents = parents;
        _passwordHasher = passwordHasher;
        _otpService = otpService;
        _notifications = notifications;
        _accessTokenIssuer = accessTokenIssuer;
        _refreshTokens = refreshTokens;
        _staffUsers = staffUsers;
        _affiliations = affiliations;
        _permissionTemplates = permissionTemplates;
        _overrides = overrides;
    }

    // ── Register: creates an Identity (claim-aware) — never a role ────────────────────────────

    public async Task RegisterAsync(UnifiedRegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new AppErrorException("First and last name are required.", 400, ErrorCodes.ValidationError);
        }

        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            throw new AppErrorException("Enter a valid Nigerian phone number.", 400, ErrorCodes.ValidationError);
        }

        if (request.Password.Length < MinPasswordLength)
        {
            throw new AppErrorException($"Password must be at least {MinPasswordLength} characters.",
                400, ErrorCodes.ValidationError);
        }

        string firstName = request.FirstName.Trim();
        string? middleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim();
        string lastName = request.LastName.Trim();
        string? email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        string passwordHash = _passwordHasher.Hash(request.Password);

        EduTech.Identity.Domain.Identity? existing = await _identities.GetByPhoneAsync(phone, cancellationToken);

        Guid identityId;
        if (existing is not null)
        {
            // A pending (school-seeded / backfilled) identity is claimed; a claimed one is a duplicate.
            existing.EnsureClaimable();
            int claimed = await _identities.ClaimAsync(existing.Id, firstName, middleName, lastName,
                email, passwordHash, cancellationToken);
            if (claimed == 0)
            {
                throw new AppErrorException(RegistrationFailed, 409, ErrorCodes.PhoneTaken,
                    logReason: "Unified register: identity claimed concurrently.");
            }
            identityId = existing.Id;
        }
        else
        {
            identityId = await _identities.CreateAsync(firstName, middleName, lastName, phone, email,
                passwordHash, cancellationToken);
        }

        // Registration creates an IDENTITY — nothing else (EDD-001). Profiles and contexts come from
        // relationships: a school links a child, an invite is accepted, an organization is created,
        // or the person deliberately begins the parent journey on the onboarding hub.
        // One exception: a school-seeded guardian may already OWN a parent profile — link it.
        if (await _parents.GetIdByPhoneAsync(phone, cancellationToken) is Guid existingParentId)
        {
            await _contexts.LinkParentAsync(existingParentId, identityId, cancellationToken);
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.IdentityPhoneVerification, identityId, phone, cancellationToken);
        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow verification code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task VerifyPhoneAsync(UnifiedVerifyPhoneRequest request, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null || string.IsNullOrWhiteSpace(request.Code))
        {
            throw new AppErrorException("Invalid verification request.", 400, ErrorCodes.InvalidOtp);
        }

        EduTech.Identity.Domain.Identity identity = await _identities.GetByPhoneAsync(phone, cancellationToken)
            ?? throw new AppErrorException("Invalid verification request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Unified verify: no identity for phone.");

        OtpVerifyResult result = await _otpService.VerifyAsync(
            OtpPurpose.IdentityPhoneVerification, identity.Id, request.Code.Trim(), cancellationToken);

        switch (result)
        {
            case OtpVerifyResult.Success:
                await _identities.MarkPhoneVerifiedAsync(identity.Id, cancellationToken);
                // Keep the legacy parent row verified too (transition dual-write).
                Guid? parentId = await _parents.GetIdByPhoneAsync(phone, cancellationToken);
                if (parentId is Guid pid)
                {
                    await _parents.MarkPhoneVerifiedAsync(pid, cancellationToken);
                }
                return;

            case OtpVerifyResult.Expired:
                throw new AppErrorException("Verification code has expired. Request a new one.",
                    400, ErrorCodes.OtpExpired);

            case OtpVerifyResult.TooManyAttempts:
                throw new AppErrorException("Too many incorrect attempts. Request a new code.",
                    429, ErrorCodes.TooManyRequests);

            default:
                throw new AppErrorException("The code you entered is incorrect.", 400, ErrorCodes.InvalidOtp);
        }
    }

    public async Task ResendOtpAsync(string phoneRaw, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(phoneRaw);
        if (phone is null)
        {
            throw new AppErrorException("Enter a valid Nigerian phone number.", 400, ErrorCodes.ValidationError);
        }

        EduTech.Identity.Domain.Identity? identity = await _identities.GetByPhoneAsync(phone, cancellationToken);
        if (identity is null)
        {
            return; // don't reveal whether the phone exists
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.IdentityPhoneVerification, identity.Id, phone, cancellationToken);
        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow verification code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task ForgotPasswordAsync(string phoneRaw, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(phoneRaw);
        if (phone is null)
        {
            throw new AppErrorException("Enter a valid Nigerian phone number.", 400, ErrorCodes.ValidationError);
        }

        EduTech.Identity.Domain.Identity? identity = await _identities.GetByPhoneAsync(phone, cancellationToken);
        if (identity is null || !identity.IsClaimed)
        {
            return; // don't reveal whether the phone exists / is registered
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.IdentityPasswordReset, identity.Id, phone, cancellationToken);
        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow password reset code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task ResetPasswordAsync(UnifiedResetPasswordRequest request, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null || string.IsNullOrWhiteSpace(request.Code))
        {
            throw new AppErrorException("Invalid reset request.", 400, ErrorCodes.InvalidOtp);
        }

        if (request.NewPassword.Length < MinPasswordLength)
        {
            throw new AppErrorException($"Password must be at least {MinPasswordLength} characters.",
                400, ErrorCodes.ValidationError);
        }

        EduTech.Identity.Domain.Identity identity = await _identities.GetByPhoneAsync(phone, cancellationToken)
            ?? throw new AppErrorException("Invalid reset request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Unified reset: no identity for phone.");

        OtpVerifyResult result = await _otpService.VerifyAsync(
            OtpPurpose.IdentityPasswordReset, identity.Id, request.Code.Trim(), cancellationToken);
        if (result != OtpVerifyResult.Success)
        {
            throw new AppErrorException("The code you entered is incorrect or has expired.",
                400, ErrorCodes.InvalidOtp);
        }

        string hash = _passwordHasher.Hash(request.NewPassword);
        await _identities.SetPasswordAsync(identity.Id, hash, cancellationToken);
        // The identity is the ONLY credential — profiles never authenticate (EDD-001 Sprint 5).
    }

    public async Task<UnifiedMeResponse> GetMeAsync(string userType, Guid actorId, Guid? currentContextId,
        CancellationToken cancellationToken)
    {
        Guid identityId = await ResolveIdentityIdAsync(userType, actorId, cancellationToken);
        return await GetMeByIdentityAsync(identityId, currentContextId, cancellationToken);
    }

    public async Task<Guid> ResolveIdentityIdAsync(string userType, Guid actorId, CancellationToken cancellationToken)
    {
        return await _contexts.GetIdentityIdForActorAsync(userType, actorId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound,
                logReason: "Portal actor has no linked identity.");
    }

    public async Task<OrganizationWorkspaceResponse> GetOrganizationWorkspaceAsync(Guid identityId, string slug,
        CancellationToken cancellationToken)
    {
        OrganizationRow? organization = await _contexts.GetOrganizationBySlugAsync(slug.Trim().ToLowerInvariant(),
            cancellationToken);

        // Same 404 whether the slug doesn't exist or isn't theirs — a workspace URL reveals nothing.
        AuthContextItem? myContext = organization is null
            ? null
            : (await BuildContextsAsync(identityId, cancellationToken))
                .FirstOrDefault(c => c.OrganizationId == organization.Id);
        if (organization is null || myContext is null)
        {
            throw new AppErrorException("We couldn't find that organization on your account.",
                404, ErrorCodes.NotFound, logReason: "Workspace: slug unknown or identity has no context there.");
        }

        return new OrganizationWorkspaceResponse
        {
            OrganizationId = organization.Id,
            Slug = organization.Slug,
            Name = organization.Name,
            LogoUrl = organization.LogoUrl,
            Status = organization.Status,
            KycStatus = organization.KycStatus,
            MyContext = myContext
        };
    }

    public async Task<OrganizationWorkspaceResponse> SetupOrganizationAsync(Guid identityId, string slug,
        SetupOrganizationRequest request, CancellationToken cancellationToken)
    {
        // Resolve + authorize in one move: this 404s unless the org exists AND is on the caller's account.
        OrganizationWorkspaceResponse workspace =
            await GetOrganizationWorkspaceAsync(identityId, slug, cancellationToken);

        // Only the owner names the school — a staff member in the same org must not.
        if (workspace.MyContext.Type != "owner")
        {
            throw new AppErrorException("Only the school owner can set up the organization.",
                403, ErrorCodes.Forbidden, logReason: "Org setup attempted by non-owner context.");
        }

        string name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new AppErrorException("School name is required.", 400, ErrorCodes.ValidationError);
        }

        // Re-slug from the name; keep the placeholder if the name yields nothing URL-safe, and ensure
        // uniqueness by suffixing -2, -3… (the school's own current slug never counts as a clash).
        string baseSlug = OrganizationSlug.From(name);
        string newSlug = baseSlug.Length == 0 ? workspace.Slug : baseSlug;
        for (int suffix = 2; await _contexts.SlugTakenAsync(newSlug, workspace.OrganizationId, cancellationToken); suffix++)
        {
            newSlug = $"{baseSlug}-{suffix}";
        }

        string? type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim().ToLowerInvariant();
        string? state = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim();

        await _contexts.SetOrganizationDetailsAsync(workspace.OrganizationId, name, type, state, newSlug, cancellationToken);

        // Re-resolve at the new slug so the response reflects the rename (name + slug).
        return await GetOrganizationWorkspaceAsync(identityId, newSlug, cancellationToken);
    }

    public async Task<PlatformHomeProjection> GetPlatformHomeAsync(Guid identityId, Guid? currentContextId,
        CancellationToken cancellationToken)
    {
        UnifiedMeResponse me = await GetMeByIdentityAsync(identityId, currentContextId, cancellationToken);
        WelcomeResponse welcome = await GetWelcomeAsync(identityId, cancellationToken);
        FamilySummaryRow family = await _contexts.GetFamilySummaryAsync(identityId, cancellationToken);
        IReadOnlyList<ContextRecencyRow> recency = await _contexts.ListContextRecencyAsync(identityId, cancellationToken);

        // Switcher: every ORGANIZATION workspace ordered by last entry (refresh-token issuance).
        // "Current" reflects the SESSION's token — the page decides whether to exclude it (a person
        // reading the family home isn't "inside" the workspace their token was minted for).
        Dictionary<Guid, DateTime?> lastActive = recency.ToDictionary(r => r.ContextId, r => r.LastActiveAt);
        List<WorkspaceRef> workspaces = me.Contexts
            .Where(c => c.OrganizationId is not null)
            .Select(c => new WorkspaceRef
            {
                ContextId = c.Id,
                Type = c.Type,
                Role = c.Role,
                OrganizationId = c.OrganizationId,
                OrganizationName = c.OrganizationName,
                OrganizationSlug = c.OrganizationSlug,
                LastActiveAt = lastActive.GetValueOrDefault(c.Id)
            })
            .OrderByDescending(w => w.LastActiveAt ?? DateTime.MinValue)
            .ToList();

        return new PlatformHomeProjection
        {
            Identity = new IdentitySummary
            {
                FullName = me.FullName,
                Phone = me.Phone,
                Email = me.Email,
                PhoneVerified = me.PhoneVerified
            },
            Profiles = me.Profiles,
            Capabilities = me.Capabilities,
            Organizations = me.Contexts,
            CurrentContextId = me.CurrentContextId,
            PendingInvitations = welcome.PendingInvites,
            DraftOrganizations = welcome.DraftOrganizations,
            Family = new FamilySummary
            {
                Children = family.Children,
                OpenApplications = family.OpenApplications
            },
            Switcher = new SwitcherProjection
            {
                CurrentWorkspace = workspaces.FirstOrDefault(w => w.ContextId == me.CurrentContextId),
                RecentWorkspaces = workspaces
            }
        };
    }

    public async Task<WelcomeResponse> GetWelcomeAsync(Guid identityId, CancellationToken cancellationToken)
    {
        EduTech.Identity.Domain.Identity identity = await _identities.GetByIdAsync(identityId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);

        IReadOnlyList<PendingInviteRow> invites =
            await _contexts.ListPendingInvitesByPhoneAsync(identity.Phone, cancellationToken);
        IReadOnlyList<DraftOrganizationRow> drafts =
            await _contexts.ListDraftOrganizationsAsync(identityId, cancellationToken);

        return new WelcomeResponse
        {
            PendingInvites = invites.Select(i => new PendingInviteItem
            {
                SchoolName = i.SchoolName,
                Role = i.Role,
                ExpiresAt = i.ExpiresAt
            }).ToList(),
            DraftOrganizations = drafts.Select(d => new DraftOrganizationItem
            {
                ContextId = d.ContextId,
                OrganizationId = d.OrganizationId,
                Slug = d.Slug
            }).ToList()
        };
    }

    public async Task<UnifiedMeResponse> GetMeByIdentityAsync(Guid identityId, Guid? currentContextId,
        CancellationToken cancellationToken)
    {
        EduTech.Identity.Domain.Identity identity = await _identities.GetByIdAsync(identityId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);

        IReadOnlyList<AuthContextItem> contexts = await BuildContextsAsync(identityId, cancellationToken);
        IReadOnlyList<string> profiles = await _contexts.ListProfileKindsAsync(identityId, cancellationToken);

        return new UnifiedMeResponse
        {
            FullName = string.Join(' ', new[] { identity.FirstName, identity.MiddleName, identity.LastName }
                .Where(n => !string.IsNullOrWhiteSpace(n))),
            Phone = identity.Phone,
            Email = identity.Email,
            PhoneVerified = identity.PhoneVerified,
            Profiles = profiles,
            Capabilities = await BuildCapabilitiesAsync(identity, contexts, profiles, cancellationToken),
            Contexts = contexts,
            CurrentContextId = currentContextId
        };
    }

    /// <summary>
    /// EDD-005 capability model: platform-level actions available to this identity NOW, derived
    /// from existing state — never stored. Workspace permissions are a different concern and stay
    /// inside /o/{slug}. Unverified identities can do exactly one thing: verify (empty list).
    /// </summary>
    private async Task<IReadOnlyList<string>> BuildCapabilitiesAsync(EduTech.Identity.Domain.Identity identity,
        IReadOnlyList<AuthContextItem> contexts, IReadOnlyList<string> profiles,
        CancellationToken cancellationToken)
    {
        if (!identity.PhoneVerified)
        {
            return Array.Empty<string>();
        }

        List<string> capabilities = new List<string>();

        if (!contexts.Any(c => c.Type == "owner"))
        {
            capabilities.Add("create_school"); // one school per account for now
        }
        if (contexts.Any(c => c.Type == "owner" && c.OrganizationName is null))
        {
            capabilities.Add("resume_school_setup"); // abandoned Organization Wizard
        }

        // The parent journey is open to every verified identity; the profile is created on demand.
        capabilities.Add("find_school");
        capabilities.Add("add_child");

        if (profiles.Contains("parent"))
        {
            capabilities.Add("open_family_home"); // the school-agnostic family view is live for them
        }

        IReadOnlyList<PendingInviteRow> invites =
            await _contexts.ListPendingInvitesByPhoneAsync(identity.Phone, cancellationToken);
        if (invites.Count > 0)
        {
            capabilities.Add("accept_invitation");
        }

        return capabilities;
    }

    // ── Login: one flow, contexts decide the portal ───────────────────────────────────────────

    public async Task<UnifiedLoginResult> LoginAsync(UnifiedLoginRequest request, string? ipAddress,
        string? userAgent, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            throw new AppErrorException(InvalidCredentials, 401, ErrorCodes.Unauthorized);
        }

        EduTech.Identity.Domain.Identity identity = await _identities.GetByPhoneAsync(phone, cancellationToken)
            ?? throw new AppErrorException(InvalidCredentials, 401, ErrorCodes.Unauthorized,
                logReason: "Unified login: no identity for phone.");

        DateTime now = DateTime.UtcNow;
        identity.EnsureCanAttemptLogin(now);

        if (identity.PasswordHash is null || !_passwordHasher.Verify(request.Password, identity.PasswordHash))
        {
            identity.RecordFailedLogin(now);
            await _identities.SaveLoginStateAsync(identity.Id, identity.FailedLoginCount,
                identity.LockedUntil, successful: false, cancellationToken);
            throw new AppErrorException(InvalidCredentials, 401, ErrorCodes.Unauthorized,
                logReason: "Unified login: wrong password (or unclaimed identity).");
        }

        identity.EnsureLoginComplete();
        identity.RecordSuccessfulLogin();
        await _identities.SaveLoginStateAsync(identity.Id, 0, null, successful: true, cancellationToken);

        IReadOnlyList<AuthContextItem> contexts = await BuildContextsAsync(identity.Id, cancellationToken);

        AuthContextItem? selected = contexts.Count == 1 ? contexts[0] : null;

        UnifiedTokens tokens;
        if (selected is not null)
        {
            tokens = await EnterContextAsync(identity, selected, ipAddress, userAgent, cancellationToken);
        }
        else
        {
            // Zero contexts (onboarding hub) or several (picker → /select-context): either way the
            // person IS authenticated — issue the identity-scope session. It opens no portal.
            AccessToken access = _accessTokenIssuer.IssueIdentity(identity.Id, identity.Phone);
            RefreshTokenIssue refresh = await _refreshTokens.IssueAsync("identity", identity.Id,
                ipAddress, userAgent, cancellationToken);
            tokens = Tokens(access, refresh);
        }

        return new UnifiedLoginResult { Contexts = contexts, Selected = selected?.Id, Tokens = tokens };
    }

    public async Task<UnifiedLoginResult> SelectContextAsync(string userType, Guid actorId, Guid contextId,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        Guid identityId = await _contexts.GetIdentityIdForActorAsync(userType, actorId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);
        EduTech.Identity.Domain.Identity identity = await _identities.GetByIdAsync(identityId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);

        IReadOnlyList<AuthContextItem> contexts = await BuildContextsAsync(identityId, cancellationToken);
        AuthContextItem selected = contexts.FirstOrDefault(c => c.Id == contextId)
            ?? throw new AppErrorException("That organization isn't available on this account.",
                403, ErrorCodes.Forbidden, logReason: "Select-context: id not owned by identity.");

        UnifiedTokens tokens = await EnterContextAsync(identity, selected, ipAddress, userAgent, cancellationToken);
        return new UnifiedLoginResult { Contexts = contexts, Selected = selected.Id, Tokens = tokens };
    }

    public async Task<UnifiedLoginResult> CreateOrganizationAsync(string userType, Guid actorId,
        SetupOrganizationRequest request, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken)
    {
        Guid identityId = await _contexts.GetIdentityIdForActorAsync(userType, actorId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);
        EduTech.Identity.Domain.Identity identity = await _identities.GetByIdAsync(identityId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);

        if (!identity.IsClaimed || !identity.PhoneVerified)
        {
            throw new AppErrorException("Verify your phone number before creating a school.",
                403, ErrorCodes.Forbidden);
        }

        // Form-first (FE-001): the school is born named, so an abandoned "Create a school" writes
        // nothing — there is no unnamed shell to clean up.
        string name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new AppErrorException("School name is required.", 400, ErrorCodes.ValidationError);
        }

        // Unique slug from the name; fall back to a random placeholder if the name is all non-URL-safe,
        // then suffix -2, -3… until free (Guid.Empty excludes no real school).
        string baseSlug = OrganizationSlug.From(name);
        if (baseSlug.Length == 0)
        {
            baseSlug = $"s-{Guid.NewGuid():N}"[..10];
        }
        string slug = baseSlug;
        for (int suffix = 2; await _contexts.SlugTakenAsync(slug, Guid.Empty, cancellationToken); suffix++)
        {
            slug = $"{baseSlug}-{suffix}";
        }

        string? type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim().ToLowerInvariant();
        string? state = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim();

        Guid ownerId;
        try
        {
            await using DbTransactionScope transaction =
                await _connectionFactory.BeginTransactionAsync(cancellationToken);
            Guid schoolId = await _schools.CreateNamedAsync(name, slug, type, state,
                transaction.Transaction, cancellationToken);
            // The owner employment reuses the identity's own details (transition: legacy columns
            // still filled; the identity remains the single credential source).
            ownerId = await _owners.CreateAsync(schoolId, identity.FirstName, identity.MiddleName,
                identity.LastName, identity.Phone, identity.Email, identity.PasswordHash!,
                transaction.Transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new AppErrorException("You already own a school on this account.",
                409, ErrorCodes.Conflict, logReason: "Create organization: owner phone already used.");
        }

        await _owners.MarkPhoneVerifiedAsync(ownerId, cancellationToken);
        OwnerIdentityLink ownerLink = await _contexts.EnsureOwnerIdentityLinksAsync(ownerId, cancellationToken);
        if (ownerLink.IdentityId is Guid ownerIdentityId)
        {
            await _memberships.EnsureActiveAsync(ownerIdentityId, ownerLink.SchoolId, MembershipKind.Owner,
                cancellationToken);
        }

        IReadOnlyList<AuthContextItem> contexts = await BuildContextsAsync(identityId, cancellationToken);
        AuthContextItem selected = contexts.First(c => c.Id == ownerId);
        UnifiedTokens tokens = await EnterContextAsync(identity, selected, ipAddress, userAgent, cancellationToken);
        return new UnifiedLoginResult { Contexts = contexts, Selected = selected.Id, Tokens = tokens };
    }

    private async Task<IReadOnlyList<AuthContextItem>> BuildContextsAsync(Guid identityId,
        CancellationToken cancellationToken)
    {
        // One read over the AccessContext projection (EDD-003) — login doesn't know the silos.
        IReadOnlyList<AccessContextRow> rows =
            await _contexts.ListAccessContextsAsync(identityId, cancellationToken);

        return rows.Select(r => new AuthContextItem
        {
            Id = r.ReferenceId,
            Type = r.Type,
            OrganizationId = r.OrganizationId,
            OrganizationName = r.OrganizationName,
            OrganizationSlug = r.OrganizationSlug,
            Role = r.Type == "owner" ? "owner" : r.Role
        }).ToList();
    }

    /// <summary>Mints the legacy-shaped portal tokens for the chosen context.</summary>
    public async Task<UnifiedTokens> RefreshSessionAsync(string refreshToken, string? ipAddress,
        string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AppErrorException("Missing refresh token.", 401, ErrorCodes.Unauthorized,
                logReason: "Unified refresh: no refresh cookie present.");
        }

        RefreshRotationResult rotation = await _refreshTokens.RotateAsync(
            refreshToken, ipAddress, userAgent, cancellationToken);
        if (!rotation.IsSuccess)
        {
            throw new AppErrorException("Session expired. Please log in again.", 401, ErrorCodes.Unauthorized,
                logReason: $"Unified refresh: rotation failed ({rotation.Status}).");
        }

        // The session record — never the browser URL — decides what gets minted (EDD-005 P6).
        AccessToken access = rotation.ActorType switch
        {
            "identity" => await MintIdentityAccessAsync(rotation, cancellationToken),
            AuthActorTypes.Parent => await MintParentAccessAsync(rotation, cancellationToken),
            AuthActorTypes.Staff => await MintStaffAccessAsync(rotation, cancellationToken),
            AuthActorTypes.SchoolOwner => await MintOwnerAccessAsync(rotation, cancellationToken),
            _ => throw new AppErrorException("Session expired. Please log in again.", 401, ErrorCodes.Unauthorized,
                logReason: $"Unified refresh: unsupported actor type '{rotation.ActorType}'.")
        };

        return new UnifiedTokens
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = rotation.NewToken!,
            RefreshTokenExpiresAt = rotation.ExpiresAt
        };
    }

    private async Task<AccessToken> MintIdentityAccessAsync(RefreshRotationResult rotation,
        CancellationToken cancellationToken)
    {
        EduTech.Identity.Domain.Identity? identity = await _identities.GetByIdAsync(rotation.ActorId, cancellationToken);
        if (identity is null || identity.Status != EduTech.Identity.Domain.IdentityStatus.Active)
        {
            await RevokeAndRejectAsync(rotation, "identity missing or inactive", cancellationToken);
        }

        return _accessTokenIssuer.IssueIdentity(identity!.Id, identity.Phone);
    }

    private async Task<AccessToken> MintParentAccessAsync(RefreshRotationResult rotation,
        CancellationToken cancellationToken)
    {
        ParentTokenRow? parent = await _parents.GetTokenClaimsAsync(rotation.ActorId, cancellationToken);
        if (parent is null || !parent.IsActive)
        {
            await RevokeAndRejectAsync(rotation, "parent missing or deactivated", cancellationToken);
        }

        Guid? identityId = await _contexts.GetIdentityIdForActorAsync("parent", rotation.ActorId, cancellationToken);
        return _accessTokenIssuer.IssueParent(rotation.ActorId, parent!.Phone, identityId, rotation.ActorId);
    }

    private async Task<AccessToken> MintStaffAccessAsync(RefreshRotationResult rotation,
        CancellationToken cancellationToken)
    {
        StaffUserTokenRow? staff = await _staffUsers.GetTokenClaimsAsync(rotation.ActorId, cancellationToken);
        if (staff is null || !staff.IsActive)
        {
            await RevokeAndRejectAsync(rotation, "staff missing or deactivated", cancellationToken);
        }

        // School-less staff token (same as the legacy staff refresh) — the workspace re-enters
        // its affiliation context via select-context when it needs the scoped token.
        return _accessTokenIssuer.IssueStaffIdentity(rotation.ActorId, staff!.Phone, staff.KycStatus);
    }

    private async Task<AccessToken> MintOwnerAccessAsync(RefreshRotationResult rotation,
        CancellationToken cancellationToken)
    {
        SchoolOwnerTokenRow? owner = await _owners.GetTokenClaimsAsync(rotation.ActorId, cancellationToken);
        if (owner is null || !owner.IsActive)
        {
            await RevokeAndRejectAsync(rotation, "owner missing or deactivated", cancellationToken);
        }

        SchoolStatusRow status = await _schools.GetStatusAsync(owner!.SchoolId, cancellationToken)
            ?? throw new AppErrorException("School not found.", 404, ErrorCodes.NotFound);

        Guid? identityId = await _contexts.GetIdentityIdForActorAsync("school", rotation.ActorId, cancellationToken);
        return _accessTokenIssuer.IssueSchoolOwner(rotation.ActorId, owner.SchoolId, owner.Phone,
            status.Status, status.KycStatus, status.Subdomain, identityId, rotation.ActorId);
    }

    private async Task RevokeAndRejectAsync(RefreshRotationResult rotation, string reason,
        CancellationToken cancellationToken)
    {
        await _refreshTokens.RevokeAllForActorAsync(rotation.ActorType!, rotation.ActorId, cancellationToken);
        throw new AppErrorException("Session is no longer valid. Please log in again.",
            401, ErrorCodes.Unauthorized, logReason: $"Unified refresh: {reason}.");
    }

    private async Task<UnifiedTokens> EnterContextAsync(EduTech.Identity.Domain.Identity identity,
        AuthContextItem context, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        Guid actorId = context.Id;

        switch (context.Type)
        {
            case "owner":
            {
                IReadOnlyList<OwnerContextRow> owners =
                    await _contexts.ListOwnerContextsAsync(identity.Id, cancellationToken);
                OwnerContextRow owner = owners.First(o => o.OwnerId == actorId);
                AccessToken access = _accessTokenIssuer.IssueSchoolOwner(owner.OwnerId, owner.SchoolId,
                    identity.Phone, owner.Status, owner.KycStatus, owner.Subdomain,
                    identityId: identity.Id, contextId: owner.OwnerId);
                RefreshTokenIssue refresh = await _refreshTokens.IssueAsync(AuthActorTypes.SchoolOwner,
                    owner.OwnerId, ipAddress, userAgent, cancellationToken);
                return Tokens(access, refresh);
            }

            case "staff":
            {
                IReadOnlyList<StaffContextRow> staffContexts =
                    await _contexts.ListStaffContextsAsync(identity.Id, cancellationToken);
                StaffContextRow staffContext = staffContexts.First(s => s.AffiliationId == actorId);

                // Mirrors StaffSchoolService.SwitchAsync (role → template → overrides), which needs an
                // already-authenticated staff token and so can't be called during login.
                StaffSwitchRow affiliation = await _affiliations.GetActiveForSwitchAsync(
                        staffContext.StaffUserId, staffContext.SchoolId, cancellationToken)
                    ?? throw new AppErrorException("You don't have an active role at this school.",
                        403, ErrorCodes.Forbidden);
                StaffUserTokenRow staff = await _staffUsers.GetTokenClaimsAsync(staffContext.StaffUserId, cancellationToken)
                    ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);

                IReadOnlyDictionary<string, bool>? templateFeatures =
                    affiliation.PermissionTemplateId is Guid templateId
                        ? await _permissionTemplates.GetFeaturesAsync(templateId, cancellationToken)
                        : null;
                IReadOnlyDictionary<string, bool> overrides =
                    await _overrides.GetForAffiliationAsync(affiliation.AffiliationId, cancellationToken);
                IReadOnlyDictionary<string, bool> features =
                    StaffFeatureResolver.Resolve(affiliation.Role, templateFeatures, overrides);

                AccessToken access = _accessTokenIssuer.IssueStaffScoped(staffContext.StaffUserId,
                    staffContext.SchoolId, affiliation.AffiliationId, identity.Phone,
                    affiliation.Role, affiliation.EmploymentType, staff.KycStatus, features,
                    identityId: identity.Id, contextId: affiliation.AffiliationId);
                RefreshTokenIssue refresh = await _refreshTokens.IssueAsync(AuthActorTypes.Staff,
                    staffContext.StaffUserId, ipAddress, userAgent, cancellationToken);
                return Tokens(access, refresh);
            }

            case "parent":
            {
                // Org-scoped parent context (EDD-002 revision): the token carries the school so parent
                // data binds @SchoolId + @ParentId. A legacy NULL-org context stays school-agnostic.
                AccessToken access = _accessTokenIssuer.IssueParent(actorId, identity.Phone,
                    identityId: identity.Id, contextId: actorId, schoolId: context.OrganizationId);
                RefreshTokenIssue refresh = await _refreshTokens.IssueAsync(AuthActorTypes.Parent,
                    actorId, ipAddress, userAgent, cancellationToken);
                return Tokens(access, refresh);
            }

            default:
                throw new AppErrorException("Unknown context.", 400, ErrorCodes.ValidationError);
        }
    }

    private static UnifiedTokens Tokens(AccessToken access, RefreshTokenIssue refresh) => new UnifiedTokens
    {
        AccessToken = access.Token,
        AccessTokenExpiresAt = access.ExpiresAt,
        RefreshToken = refresh.Token,
        RefreshTokenExpiresAt = refresh.ExpiresAt
    };
}
