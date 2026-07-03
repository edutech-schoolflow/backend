namespace EduTech.Shared.Constants;

public static class StaffRoles
{
    public const string SuperAdmin = "super_admin";
    public const string SchoolAdmin = "school_admin";
    public const string Principal = "principal";
    public const string VicePrincipal = "vice_principal";
    public const string Teacher = "teacher";
    public const string Bursar = "bursar";
    public const string Registrar = "registrar";

    private static readonly HashSet<string> Invitable = new HashSet<string>
    {
        SchoolAdmin, Principal, VicePrincipal, Teacher, Bursar, Registrar
    };

    /// <summary>Roles a school can assign to staff (everything except the platform super_admin).</summary>
    public static bool IsInvitable(string? role) => role is not null && Invitable.Contains(role);
}

/// <summary>
/// Identifies which portal a JWT belongs to.
/// Embedded as the "user_type" claim in every token.
/// </summary>
public static class UserTypes
{
    /// <summary>Teachers, bursars, registrars etc. — staff.schoolflow.com</summary>
    public const string Staff = "staff";

    /// <summary>Parents logging in via phone OTP — parent.schoolflow.com</summary>
    public const string Parent = "parent";

    /// <summary>
    /// The school owner/account admin who registered the school.
    /// Identified by isOwner === true in the DB.
    /// Accesses the school subdomain (e.g. greenfield.schoolflow.com).
    /// </summary>
    public const string School = "school";

    /// <summary>
    /// SchoolFlow platform super-admin who oversees all school accounts.
    /// Frontend portal not yet built — plan for it now.
    /// Accesses admin.schoolflow.com.
    /// </summary>
    public const string PlatformAdmin = "platform_admin";
}
