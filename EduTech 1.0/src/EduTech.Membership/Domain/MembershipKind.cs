namespace EduTech.Membership.Domain;

/// <summary>
/// The canonical adult membership kinds (EDD-007). A membership is the belonging edge between an
/// Identity and an Organization; <c>kind</c> says in what capacity. Students belong as a Child
/// Profile (the <c>students</c> edge), not as one of these identity-based kinds.
///
/// These mirror the <c>memberships_kind_chk</c> constraint (migration 0045) — keep them in step.
/// </summary>
public static class MembershipKind
{
    public const string Parent = "parent";
    public const string Staff = "staff";
    public const string Owner = "owner";
    public const string Vendor = "vendor";
    public const string Governor = "governor";
    public const string Pta = "pta";
    public const string Volunteer = "volunteer";
    public const string Alumni = "alumni";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Parent, Staff, Owner, Vendor, Governor, Pta, Volunteer, Alumni
    };

    public static bool IsValid(string? kind) => kind is not null && All.Contains(kind);
}
