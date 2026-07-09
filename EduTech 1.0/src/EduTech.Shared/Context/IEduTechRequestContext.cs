namespace EduTech.Shared.Context;

/// <summary>
/// Injectable accessor for the current authenticated user's identity.
/// Populated from JWT claims — never pass HttpContext into services; use this instead.
///
/// Portals:
///   Staff        — staff.schoolflow.com           user_type = "staff"
///   School Owner — {subdomain}.schoolflow.com     user_type = "school"  (isOwner = true)
///   Parent       — parent.schoolflow.com           user_type = "parent"
///   Platform Admin — admin.schoolflow.com          user_type = "platform_admin" (future)
/// </summary>
public interface IEduTechRequestContext
{
    string? UserId { get; }

    /// <summary>"staff" | "school" | "parent" | "platform_admin"</summary>
    string? UserType { get; }

    /// <summary>StaffRole value, "parent", or "platform_admin"</summary>
    string? Role { get; }

    /// <summary>The school this user belongs to. Null for platform admins.</summary>
    string? SchoolId { get; }

    /// <summary>
    /// The staff member's affiliation (staff_affiliations.id) at the active school. Present only on
    /// SCHOOL-SCOPED staff tokens; null for owners, parents, and platform admins. Used to scope
    /// staff actions to the records they own (e.g. a class teacher marking only their arm's register).
    /// </summary>
    string? AffiliationId { get; }

    /// <summary>The person (identities.id) behind this request — org-context tokens carry it.</summary>
    string? IdentityId { get; }

    /// <summary>The active AccessContext reference this session operates in.</summary>
    string? ContextId { get; }

    /// <summary>
    /// True only for the school account owner (registered the school).
    /// Bypasses all feature flag and role checks.
    /// </summary>
    bool IsOwner { get; }

    bool IsStaff { get; }
    bool IsSchoolOwner { get; }
    bool IsParent { get; }
    bool IsPlatformAdmin { get; }
}
