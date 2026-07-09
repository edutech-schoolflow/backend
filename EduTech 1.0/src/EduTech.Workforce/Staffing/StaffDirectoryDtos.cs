namespace EduTech.Workforce;

/// <summary>One staff member in the school directory (the affiliation + their identity).</summary>
public sealed class StaffDirectoryItemResponse
{
    public required Guid Id { get; init; }            // affiliation id (the per-school record)
    public required Guid StaffUserId { get; init; }
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public required string Phone { get; init; }
    public string? Email { get; init; }
    public required string Role { get; init; }
    public string? Position { get; init; }
    public required string EmploymentType { get; init; }
    public required string Status { get; init; }      // invited | active | inactive
    public DateTime? JoinedAt { get; init; }
    public required DateTime CreatedAt { get; init; }  // when invited
}

/// <summary>Update a staff member's role and/or position within the school.</summary>
public sealed class UpdateStaffRoleRequest
{
    public string Role { get; init; } = string.Empty;
    public string? Position { get; init; }
}
