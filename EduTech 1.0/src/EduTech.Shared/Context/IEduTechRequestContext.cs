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
    /// True only for the school account owner (registered the school).
    /// Bypasses all feature flag and role checks.
    /// </summary>
    bool IsOwner { get; }

    bool IsStaff { get; }
    bool IsSchoolOwner { get; }
    bool IsParent { get; }
    bool IsPlatformAdmin { get; }
}
