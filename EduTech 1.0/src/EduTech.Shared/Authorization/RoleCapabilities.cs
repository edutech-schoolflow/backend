using EduTech.Shared.Constants;

namespace EduTech.Shared.Authorization;

/// <summary>
/// The canonical role → capabilities map (EDD-006) — the source of truth for what each staff role
/// can do by default. Supersedes <c>StaffRoleFeatures</c>, which is now a legacy projection of this
/// map onto the 13 feature flags. A school can still narrow or widen a member's capabilities via a
/// permission template or per-staff overrides; these are just the sensible starting point.
/// </summary>
public static class RoleCapabilities
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Defaults =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [StaffRoles.Teacher] = new[]
            {
                Capabilities.Classes.ViewMine, Capabilities.Attendance.Record,
                Capabilities.Grades.Enter, Capabilities.Grades.SubmitExamPapers
            },
            [StaffRoles.Principal] = new[]
            {
                Capabilities.School.ViewOverview, Capabilities.StaffAttendance.ViewBoard,
                Capabilities.Student.Read, Capabilities.Admissions.Manage,
                Capabilities.Fees.Invoice.View, Capabilities.Store.View
            },
            [StaffRoles.VicePrincipal] = new[]
            {
                Capabilities.School.ViewOverview, Capabilities.StaffAttendance.ViewBoard,
                Capabilities.Student.Read, Capabilities.Fees.Invoice.View,
                Capabilities.Store.View
            },
            [StaffRoles.Bursar] = new[]
            {
                Capabilities.School.ViewOverview, Capabilities.Fees.Manage,
                Capabilities.Fees.Invoice.View, Capabilities.Student.Read,
                Capabilities.Store.View, Capabilities.Store.Manage
            },
            [StaffRoles.Registrar] = new[]
            {
                Capabilities.Admissions.Manage, Capabilities.Student.Read,
                Capabilities.Store.View
            },
            [StaffRoles.SchoolAdmin] = new[]
            {
                Capabilities.StaffAttendance.ViewBoard, Capabilities.Student.Read,
                Capabilities.Fees.Invoice.View, Capabilities.Permissions.Manage,
                Capabilities.Store.View, Capabilities.Store.Manage
            },
            [StaffRoles.SuperAdmin] = CapabilityRegistry.All.Select(c => c.Key).ToArray()
        };

    /// <summary>The default capability set for a role (empty if the role is unknown).</summary>
    public static IReadOnlyList<string> For(string role) =>
        Defaults.TryGetValue(role, out IReadOnlyList<string>? caps) ? caps : Array.Empty<string>();
}
