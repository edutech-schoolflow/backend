namespace EduTech.Auth.Unified;

// EDD-001 — one registration, one login. Registration creates an Identity and NOTHING else; login
// authenticates the Identity and returns its CONTEXTS (employments + memberships). /select-context
// enters one, minting the matching portal token — every existing [Authorize] policy keeps working.

public sealed class UnifiedRegisterRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? Email { get; init; }
}

public sealed class UnifiedVerifyPhoneRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

public sealed class UnifiedResendOtpRequest
{
    public string Phone { get; init; } = string.Empty;
}

public sealed class UnifiedLoginRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>Enter one of the signed-in identity's contexts (POST /auth/select-context).</summary>
public sealed class SelectContextRequest
{
    public Guid ContextId { get; init; }
}

public sealed class UnifiedForgotPasswordRequest
{
    public string Phone { get; init; } = string.Empty;
}

public sealed class UnifiedResetPasswordRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>The signed-in person, portal-agnostic: who they are plus every context they hold.</summary>
public sealed class UnifiedMeResponse
{
    public required string FullName { get; init; }
    public required string Phone { get; init; }
    public string? Email { get; init; }
    public required bool PhoneVerified { get; init; }
    /// <summary>Profile kinds this identity owns (e.g. "parent", "staff") — not the same as contexts.</summary>
    public required IReadOnlyList<string> Profiles { get; init; }
    /// <summary>Platform-level actions available to this identity right now (EDD-005) — derived,
    /// never stored. The frontend renders what it receives instead of asking "am I a parent?".</summary>
    public required IReadOnlyList<string> Capabilities { get; init; }
    public required IReadOnlyList<AuthContextItem> Contexts { get; init; }
    /// <summary>The context this session is currently inside (the token's context_id), or null for an
    /// identity-scope session. Lets the switcher show the current workspace and list only the others.</summary>
    public Guid? CurrentContextId { get; init; }
}

/// <summary>
/// EDD-005 — the ONE rich identity projection behind the platform home: who I am, what I can do,
/// where I belong, and what's waiting for me. The landing experience renders entirely from this.
/// </summary>
public sealed class PlatformHomeProjection
{
    public required IdentitySummary Identity { get; init; }
    public required IReadOnlyList<string> Profiles { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }
    public required IReadOnlyList<AuthContextItem> Organizations { get; init; }
    public Guid? CurrentContextId { get; init; }
    public required IReadOnlyList<PendingInviteItem> PendingInvitations { get; init; }
    public required IReadOnlyList<DraftOrganizationItem> DraftOrganizations { get; init; }
    public required FamilySummary Family { get; init; }
    /// <summary>Slack-style workspace switcher: the token's current workspace + every workspace
    /// ordered by last activity. "Current" is the SESSION's — the page decides what to exclude.</summary>
    public required SwitcherProjection Switcher { get; init; }
}

public sealed class SwitcherProjection
{
    public WorkspaceRef? CurrentWorkspace { get; init; }
    public required IReadOnlyList<WorkspaceRef> RecentWorkspaces { get; init; }
}

public sealed class WorkspaceRef
{
    public required Guid ContextId { get; init; }
    public required string Type { get; init; }          // owner | staff | parent
    public string? Role { get; init; }
    public Guid? OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public string? OrganizationSlug { get; init; }
    public DateTime? LastActiveAt { get; init; }
}

public sealed class IdentitySummary
{
    public required string FullName { get; init; }
    public required string Phone { get; init; }
    public string? Email { get; init; }
    public required bool PhoneVerified { get; init; }
}

public sealed class FamilySummary
{
    public int Children { get; init; }
    public int OpenApplications { get; init; }
}

/// <summary>
/// One organization relationship the identity can act in — structured, no string keys to parse.
/// <see cref="Id"/> is the relationship row's id (owner / affiliation / parent profile id today;
/// employment / membership id after the Workforce extraction).
/// </summary>
public sealed class AuthContextItem
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }        // owner | staff | parent
    public Guid? OrganizationId { get; init; }
    /// <summary>Canonical identity of the context — the Membership it belongs to (EDD-012 B2c.1).</summary>
    public Guid? MembershipId { get; init; }
    public string? OrganizationName { get; init; }
    /// <summary>URL identity of the workspace — /o/{slug} (FE-001 Phase 2).</summary>
    public string? OrganizationSlug { get; init; }
    public string? Role { get; init; }                // staff role / "owner" / null for parent
}

/// <summary>What the login endpoint returns (tokens travel as httpOnly cookies, never in the body).</summary>
public sealed class UnifiedLoginResponse
{
    public required IReadOnlyList<AuthContextItem> Contexts { get; init; }
    /// <summary>The entered context's id, or null when the caller must pick one (or has none).</summary>
    public Guid? Selected { get; init; }
}

public sealed class UnifiedTokens
{
    public required string AccessToken { get; init; }
    public required DateTime AccessTokenExpiresAt { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime RefreshTokenExpiresAt { get; init; }
}

public sealed class UnifiedLoginResult
{
    public required IReadOnlyList<AuthContextItem> Contexts { get; init; }
    public Guid? Selected { get; init; }
    public UnifiedTokens? Tokens { get; init; }
}

/// <summary>A workspace URL resolved for the signed-in identity (FE-001 Phase 2, /o/{slug}).</summary>
public sealed class OrganizationWorkspaceResponse
{
    public required Guid OrganizationId { get; init; }
    public required string Slug { get; init; }
    /// <summary>Null until the Organization Wizard names the school — the workspace shows setup.</summary>
    public string? Name { get; init; }
    public string? LogoUrl { get; init; }
    public required string Status { get; init; }
    public required string KycStatus { get; init; }
    /// <summary>The caller's own context in this organization — drives navigation.</summary>
    public required AuthContextItem MyContext { get; init; }
}

/// <summary>
/// The Organization Wizard's payload — names a bootstrapped org (fixes the null name) and captures
/// the type that drives class provisioning. Naming re-slugs the workspace, so the response carries
/// the new slug for the caller to route to.
/// </summary>
public sealed class SetupOrganizationRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Type { get; init; }     // nursery | primary | secondary | combined (drives class ladder)
    public string? State { get; init; }
}

/// <summary>What /welcome offers a signed-in identity beyond the static hub cards.</summary>
public sealed class WelcomeResponse
{
    public required IReadOnlyList<PendingInviteItem> PendingInvites { get; init; }
    public required IReadOnlyList<DraftOrganizationItem> DraftOrganizations { get; init; }
}

/// <summary>An unaccepted staff invite addressed to this phone — the accept link lives in the SMS.</summary>
public sealed class PendingInviteItem
{
    public string? SchoolName { get; init; }
    public required string Role { get; init; }
    public required DateTime ExpiresAt { get; init; }
}

/// <summary>A bootstrapped organization that never finished the wizard — /welcome offers to resume it.</summary>
public sealed class DraftOrganizationItem
{
    public required Guid ContextId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required string Slug { get; init; }
}
