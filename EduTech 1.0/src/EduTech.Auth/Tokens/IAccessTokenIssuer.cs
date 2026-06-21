namespace EduTech.Auth.Tokens;

/// <summary>
/// Mints portal-specific JWT access tokens from the configured signing keys. Pairs with
/// <c>IRefreshTokenService</c> to form an access+refresh pair at login/refresh. Grows a method
/// per actor as each slice is built.
/// </summary>
internal interface IAccessTokenIssuer
{
 
    AccessToken IssueSchoolOwner(Guid ownerId, Guid schoolId, string phone,
        string schoolStatus, string kycStatus, string? subdomain);

  
    AccessToken IssueStaffIdentity(Guid staffUserId, string phone, string kycStatus);

    /// <summary>
    /// School-scoped staff token for the active affiliation — carries role, employment type, and the
    /// 13 resolved feature flags.
    /// </summary>
    AccessToken IssueStaffScoped(Guid staffUserId, Guid schoolId, Guid affiliationId, string phone,
        string role, string employmentType, string kycStatus, IReadOnlyDictionary<string, bool> features);

    /// <summary>Parent token — school-agnostic (no school_id claim).</summary>
    AccessToken IssueParent(Guid parentId, string phone);

    /// <summary>Internal Platform Admin token. <paramref name="role"/> is the sub-role.</summary>
    AccessToken IssuePlatformAdmin(Guid adminId, string role, string email);
}
