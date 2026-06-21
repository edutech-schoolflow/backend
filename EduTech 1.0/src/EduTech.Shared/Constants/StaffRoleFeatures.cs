namespace EduTech.Shared.Constants;

/// <summary>
/// Default feature flags per staff role — the base when no permission template is assigned. Mirrors
/// the frontend ROLE_FEATURES (spec §2.7). A school can override these via a template or per-staff
/// overrides; these are just the sensible starting point.
/// </summary>
public static class StaffRoleFeatures
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Defaults =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [StaffRoles.Teacher] = new[]
            {
                StaffFeatureFlags.ViewMyClasses, StaffFeatureFlags.MarkStudentAttendance,
                StaffFeatureFlags.EnterGrades, StaffFeatureFlags.SubmitExamPapers
            },
            [StaffRoles.Principal] = new[]
            {
                StaffFeatureFlags.ViewSchoolOverview, StaffFeatureFlags.ViewStaffAttendanceBoard,
                StaffFeatureFlags.ViewStudentRecords, StaffFeatureFlags.ManageAdmissions,
                StaffFeatureFlags.ViewInvoices, StaffFeatureFlags.ViewStore
            },
            [StaffRoles.VicePrincipal] = new[]
            {
                StaffFeatureFlags.ViewSchoolOverview, StaffFeatureFlags.ViewStaffAttendanceBoard,
                StaffFeatureFlags.ViewStudentRecords, StaffFeatureFlags.ViewInvoices,
                StaffFeatureFlags.ViewStore
            },
            [StaffRoles.Bursar] = new[]
            {
                StaffFeatureFlags.ViewSchoolOverview, StaffFeatureFlags.ManageFees,
                StaffFeatureFlags.ViewInvoices, StaffFeatureFlags.ViewStudentRecords,
                StaffFeatureFlags.ViewStore, StaffFeatureFlags.ManageStore
            },
            [StaffRoles.Registrar] = new[]
            {
                StaffFeatureFlags.ManageAdmissions, StaffFeatureFlags.ViewStudentRecords,
                StaffFeatureFlags.ViewStore
            },
            [StaffRoles.SchoolAdmin] = new[]
            {
                StaffFeatureFlags.ViewStaffAttendanceBoard, StaffFeatureFlags.ViewStudentRecords,
                StaffFeatureFlags.ViewInvoices, StaffFeatureFlags.ManagePermissions,
                StaffFeatureFlags.ViewStore, StaffFeatureFlags.ManageStore
            },
            [StaffRoles.SuperAdmin] = StaffFeatureFlags.All
        };

    /// <summary>The default enabled-flag set for a role (empty if the role is unknown).</summary>
    public static IReadOnlyList<string> For(string role) =>
        Defaults.TryGetValue(role, out IReadOnlyList<string>? flags) ? flags : Array.Empty<string>();
}
