using EduTech.Shared.Constants;

namespace EduTech.Students.Classes.Domain;

/// <summary>One standard grade on the Nigerian ladder: its display name, stage, and global order.</summary>
public sealed record StandardGrade(string Name, ClassLevel Stage, int Order);

/// <summary>
/// The platform's model of the standard Nigerian 6-3-3 education ladder — the single source of truth for
/// the ordered grades (Nursery → Primary 1–6 → JSS 1–3 → SSS 1–3), which grades a school of a given type
/// runs, and "what comes next" for promotion. Names match the frontend's class-level list so the two
/// stay in sync. Pure and stateless.
/// </summary>
public static class NigerianEducationLadder
{
    // Global order across the whole ladder — Primary 6 (8) → JSS 1 (9), JSS 3 (11) → SSS 1 (12),
    // SSS 3 (14) is terminal (its next step is graduation).
    private static readonly StandardGrade[] Ladder =
    {
        new StandardGrade("Nursery 1", ClassLevel.Nursery, 1),
        new StandardGrade("Nursery 2", ClassLevel.Nursery, 2),
        new StandardGrade("Primary 1", ClassLevel.Primary, 3),
        new StandardGrade("Primary 2", ClassLevel.Primary, 4),
        new StandardGrade("Primary 3", ClassLevel.Primary, 5),
        new StandardGrade("Primary 4", ClassLevel.Primary, 6),
        new StandardGrade("Primary 5", ClassLevel.Primary, 7),
        new StandardGrade("Primary 6", ClassLevel.Primary, 8),
        new StandardGrade("JSS 1", ClassLevel.JuniorSecondary, 9),
        new StandardGrade("JSS 2", ClassLevel.JuniorSecondary, 10),
        new StandardGrade("JSS 3", ClassLevel.JuniorSecondary, 11),
        new StandardGrade("SSS 1", ClassLevel.SeniorSecondary, 12),
        new StandardGrade("SSS 2", ClassLevel.SeniorSecondary, 13),
        new StandardGrade("SSS 3", ClassLevel.SeniorSecondary, 14),
    };

    public static IReadOnlyList<StandardGrade> All => Ladder;

    /// <summary>The grade after this one — null at SSS 3, whose "next" is graduation.</summary>
    public static StandardGrade? NextGrade(StandardGrade grade) =>
        Ladder.FirstOrDefault(g => g.Order == grade.Order + 1);

    /// <summary>SSS 3 — the final grade; promoting past it graduates the student.</summary>
    public static bool IsTerminal(StandardGrade grade) => grade.Order == Ladder[^1].Order;

    public static bool TryGetByName(string? name, out StandardGrade grade)
    {
        grade = Ladder.FirstOrDefault(g =>
            string.Equals(g.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return grade is not null;
    }

    /// <summary>
    /// The grades a school of the given type runs: nursery → Nursery; primary → Primary 1–6;
    /// secondary → JSS + SSS; combined (or unknown) → the whole ladder.
    /// </summary>
    public static IReadOnlyList<StandardGrade> GradesForType(string? schoolType)
    {
        return (schoolType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "nursery" => ByStages(ClassLevel.PreSchool, ClassLevel.Nursery),
            "primary" => ByStages(ClassLevel.Primary),
            "secondary" => ByStages(ClassLevel.JuniorSecondary, ClassLevel.SeniorSecondary),
            _ => Ladder, // "combined" or anything unrecognised → the full ladder
        };
    }

    private static IReadOnlyList<StandardGrade> ByStages(params ClassLevel[] stages) =>
        Ladder.Where(g => stages.Contains(g.Stage)).ToList();
}
