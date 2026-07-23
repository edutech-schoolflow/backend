namespace EduTech.Shared.Constants;

/// <summary>
/// The type of a working relationship (EDD-009 Employment). full_time is exclusive platform-wide
/// (a full-timer can hold no other affiliation); the others may span multiple organizations. The
/// employments table CHECK mirrors this set — keep them in step.
/// </summary>
public static class EmploymentTypes
{
    public const string FullTime = "full_time";
    public const string PartTime = "part_time";
    public const string Contract = "contract";
    public const string Temporary = "temporary";
    public const string Volunteer = "volunteer";
    public const string Intern = "intern";
    public const string Consultant = "consultant";

    public static readonly IReadOnlyList<string> All = new[]
    {
        FullTime, PartTime, Contract, Temporary, Volunteer, Intern, Consultant
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
