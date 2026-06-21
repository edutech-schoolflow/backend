namespace EduTech.Shared.Features;

/// <summary>
/// Known RELEASE feature keys — operational on/off switches for whole product features (distinct
/// from the per-staff <c>StaffFeatureFlags</c> permission flags). New features are seeded here
/// (default OFF) and shipped dark; the Platform Admin CMS turns them on per the rollout plan.
/// </summary>
public static class FeatureKeys
{
    public const string Fees = "fees";
    public const string Attendance = "attendance";
    public const string Students = "students";
    public const string Grades = "grades";
    public const string Store = "store";
    public const string Compliance = "compliance";

    /// <summary>All keys seeded into <c>feature_flags</c> (default disabled) and listed in the CMS.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Fees, Attendance, Students, Grades, Store, Compliance
    };
}
