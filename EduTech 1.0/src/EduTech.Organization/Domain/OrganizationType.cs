namespace EduTech.Organization.Domain;

/// <summary>
/// The kind of institution an Organization is (EDD-010). SchoolFlow is an education *platform*, not a
/// school-management system — only <c>School</c> is used today, but the enum future-proofs years of
/// work for ~zero cost. Mirrors the <c>organizations_type_chk</c> constraint (migration 0047).
/// </summary>
public static class OrganizationType
{
    public const string School = "school";
    public const string University = "university";
    public const string TrainingCentre = "training_centre";
    public const string Tutor = "tutor";
    public const string Corporate = "corporate";
    public const string Ngo = "ngo";

    public static readonly IReadOnlyList<string> All = new[]
    {
        School, University, TrainingCentre, Tutor, Corporate, Ngo
    };

    public static bool IsValid(string? type) => type is not null && All.Contains(type);
}
