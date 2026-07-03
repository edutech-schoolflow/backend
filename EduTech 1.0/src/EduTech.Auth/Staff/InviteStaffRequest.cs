namespace EduTech.Auth.Staff;

/// <summary>
/// A school invites a staff member by phone. Email is optional (phone-first). employmentType is
/// "full_time" or "part_time" and is enforced for exclusivity on acceptance.
/// </summary>
public sealed class InviteStaffRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string EmploymentType { get; init; } = string.Empty;
    public string? Position { get; init; }
}
