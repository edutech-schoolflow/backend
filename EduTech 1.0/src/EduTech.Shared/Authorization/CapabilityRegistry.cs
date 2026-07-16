using EduTech.Shared.Constants;

namespace EduTech.Shared.Authorization;

/// <summary>
/// The authoritative catalog of every capability (EDD-006) — the single place that knows a
/// capability's display name, description, owning module, and (during migration) its legacy flag.
///
/// <para>This registry is what makes capabilities discoverable: it unlocks an admin permission UI,
/// audit reports, and permission documentation <b>without touching authorization</b>. Enforcement
/// (<c>[RequireCapability]</c>) and token minting read the legacy flag <i>through</i> this registry,
/// so the capability stays canonical and the flag stays an implementation detail.</para>
/// </summary>
#pragma warning disable CS0618 // legacy StaffFeatureFlags are referenced here by design (the bridge)
public static class CapabilityRegistry
{
    /// <summary>Every capability in the system, seeded 1:1 with the 13 legacy flags for Sprint A.</summary>
    public static readonly IReadOnlyList<CapabilityDefinition> All = new[]
    {
        new CapabilityDefinition(Capabilities.Student.Read,
            "View student records", "Read access to student profiles and records.",
            "Students", StaffFeatureFlags.ViewStudentRecords),

        new CapabilityDefinition(Capabilities.Attendance.Record,
            "Record student attendance", "Mark and edit the student attendance register.",
            "Attendance", StaffFeatureFlags.MarkStudentAttendance),

        new CapabilityDefinition(Capabilities.Grades.Enter,
            "Enter grades", "Enter and edit student scores.",
            "Grades", StaffFeatureFlags.EnterGrades),

        new CapabilityDefinition(Capabilities.Grades.SubmitExamPapers,
            "Submit exam papers", "Submit examination papers for processing.",
            "Grades", StaffFeatureFlags.SubmitExamPapers),

        new CapabilityDefinition(Capabilities.Classes.ViewMine,
            "View my classes", "See the classes assigned to the signed-in staff member.",
            "Classes", StaffFeatureFlags.ViewMyClasses),

        new CapabilityDefinition(Capabilities.Fees.Manage,
            "Manage fees", "Create and manage fee structures and charges.",
            "Fees", StaffFeatureFlags.ManageFees),

        new CapabilityDefinition(Capabilities.Fees.Invoice.View,
            "View invoices", "Read access to student invoices.",
            "Fees", StaffFeatureFlags.ViewInvoices),

        new CapabilityDefinition(Capabilities.Admissions.Manage,
            "Manage admissions", "Review and process admission applications.",
            "Admissions", StaffFeatureFlags.ManageAdmissions),

        new CapabilityDefinition(Capabilities.School.ViewOverview,
            "View school overview", "See the school dashboard and summary metrics.",
            "School", StaffFeatureFlags.ViewSchoolOverview),

        new CapabilityDefinition(Capabilities.StaffAttendance.ViewBoard,
            "View staff attendance board", "See the staff attendance board.",
            "Workforce", StaffFeatureFlags.ViewStaffAttendanceBoard),

        new CapabilityDefinition(Capabilities.Permissions.Manage,
            "Manage permissions", "Assign roles, templates, and per-staff permission overrides.",
            "Workforce", StaffFeatureFlags.ManagePermissions),

        new CapabilityDefinition(Capabilities.Store.View,
            "View store", "Read access to the school store.",
            "Store", StaffFeatureFlags.ViewStore),

        new CapabilityDefinition(Capabilities.Store.Manage,
            "Manage store", "Manage the school store catalog and inventory.",
            "Store", StaffFeatureFlags.ManageStore),
    };

    private static readonly IReadOnlyDictionary<string, CapabilityDefinition> ByKeyIndex =
        All.ToDictionary(c => c.Key);

    /// <summary>The definition for a capability key, or null if it isn't registered.</summary>
    public static CapabilityDefinition? ByKey(string key) =>
        ByKeyIndex.TryGetValue(key, out CapabilityDefinition? def) ? def : null;

    /// <summary>
    /// The legacy feature flag a capability currently projects to, or null once the flag is retired.
    /// Throws if the key isn't registered — an unregistered capability is a programming error.
    /// </summary>
    public static string? LegacyFlagFor(string capabilityKey)
    {
        CapabilityDefinition def = ByKey(capabilityKey)
            ?? throw new ArgumentException($"Unknown capability '{capabilityKey}'.", nameof(capabilityKey));
        return def.LegacyFlag;
    }
}
#pragma warning restore CS0618
