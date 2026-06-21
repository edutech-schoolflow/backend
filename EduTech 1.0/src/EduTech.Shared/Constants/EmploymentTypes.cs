namespace EduTech.Shared.Constants;

/// <summary>
/// Staff employment type on an affiliation. full_time is exclusive platform-wide (a full-timer can
/// hold no other affiliation); part_time may span multiple schools.
/// </summary>
public static class EmploymentTypes
{
    public const string FullTime = "full_time";
    public const string PartTime = "part_time";

    public static bool IsValid(string? value) => value is FullTime or PartTime;
}
