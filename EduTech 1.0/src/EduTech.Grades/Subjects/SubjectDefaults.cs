using EduTech.Shared.Constants;

namespace EduTech.Grades.Subjects;

/// <summary>
/// Standard Nigerian subject lists per school level — used to seed a class's catalog and to suggest
/// subjects in the UI. Schools can add/remove from here; these are just sensible starting points.
/// </summary>
internal static class SubjectDefaults
{
    public static IReadOnlyList<string> ForLevel(ClassLevel level) => level switch
    {
        ClassLevel.PreSchool or ClassLevel.Nursery => Nursery,
        ClassLevel.Primary => Primary,
        ClassLevel.JuniorSecondary => JuniorSecondary,
        ClassLevel.SeniorSecondary => SeniorSecondary,
        _ => Array.Empty<string>()
    };

    private static readonly string[] Nursery =
    {
        "English Language", "Mathematics", "Phonics", "CRK/IRS", "French"
    };

    private static readonly string[] Primary =
    {
        "English Language", "Mathematics", "Basic Science", "Social Studies", "CRK/IRS",
        "Computer Studies", "Physical Education", "Verbal Reasoning", "Quantitative Reasoning"
    };

    private static readonly string[] JuniorSecondary =
    {
        "English Language", "Mathematics", "Basic Science", "Basic Technology", "Social Studies",
        "CRK/IRS", "Agricultural Science", "Computer Studies", "French", "Civic Education"
    };

    private static readonly string[] SeniorSecondary =
    {
        "English Language", "Mathematics", "Physics", "Chemistry", "Biology", "Agricultural Science",
        "Geography", "Government", "Economics", "Accounting", "Literature in English",
        "Further Mathematics", "Civic Education"
    };
}
