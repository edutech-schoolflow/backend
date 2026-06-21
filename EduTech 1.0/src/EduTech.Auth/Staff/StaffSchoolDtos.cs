namespace EduTech.Auth.Staff;

/// <summary>One entry in the staff member's "My Schools" list.</summary>
public sealed class StaffSchoolItem
{
    public required Guid SchoolId { get; init; }
    public string? SchoolName { get; init; }
    public required string Role { get; init; }
    public string? Position { get; init; }
    public required string EmploymentType { get; init; }
}

/// <summary>Result of switching schools — the new scoped access token (controller sets the cookie).</summary>
public sealed class StaffSwitchResult
{
    public required string AccessToken { get; init; }
    public required DateTime AccessTokenExpiresAt { get; init; }
}
