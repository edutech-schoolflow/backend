namespace EduTech.Auth.Tokens;

/// <summary>
/// Mints portal-specific JWT access tokens from the configured signing keys. Pairs with
/// <c>IRefreshTokenService</c> to form an access+refresh pair at login/refresh. Grows a method
/// per actor as each slice is built.
/// </summary>
internal interface IAccessTokenIssuer
{
 
    AccessToken IssueSchoolOwner(Guid ownerId, Guid schoolId, string phone,
        string schoolStatus, string kycStatus, string? subdomain,
        Guid? identityId = null, Guid? contextId = null,
        Guid? membershipId = null, Guid? organizationId = null);

  
    AccessToken IssueStaffIdentity(Guid staffUserId, string phone, string kycStatus);

    /// <summary>
    /// School-scoped staff token for the active affiliation — carries identity, context, membership,
    /// organization, role. Authorization is resolved server-side from context_id (no flags in the token).
    /// </summary>
    AccessToken IssueStaffScoped(Guid staffUserId, Guid schoolId, Guid affiliationId, string phone,
        string role, string employmentType, string kycStatus,
        Guid? identityId = null, Guid? contextId = null,
        Guid? membershipId = null, Guid? organizationId = null);

    /// <summary>Identity-scope token — a person with no organization context yet (onboarding hub).</summary>
    AccessToken IssueIdentity(Guid identityId, string phone);

    /// <summary>Parent token — school-agnostic (no school_id claim).</summary>
    // schoolId makes a parent token ORGANIZATION-SCOPED (EDD-002 revision): a parent membership belongs
    // to one school, like staff employment. Null keeps the legacy school-agnostic token (strangler path).
    AccessToken IssueParent(Guid parentId, string phone, Guid? identityId = null, Guid? contextId = null,
        Guid? schoolId = null, Guid? membershipId = null, Guid? organizationId = null);

    /// <summary>Internal Platform Admin token. <paramref name="role"/> is the sub-role.</summary>
    AccessToken IssuePlatformAdmin(Guid adminId, string role, string email);
}
