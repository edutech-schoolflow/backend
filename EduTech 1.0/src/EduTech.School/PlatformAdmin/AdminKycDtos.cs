namespace EduTech.School.PlatformAdmin;

/// <summary>A school in the admin KYC queue/detail (with its owner).</summary>
public sealed class AdminSchoolItem
{
    public required Guid SchoolId { get; init; }
    public string? Name { get; init; }
    public string? Subdomain { get; init; }
    public required string Status { get; init; }
    public required string KycStatus { get; init; }
    public string? OwnerName { get; init; }
    public string? OwnerPhone { get; init; }
    public string? OwnerEmail { get; init; }
    public required DateTime CreatedAt { get; init; }
}

/// <summary>Approve a school. Optionally assign/provision its subdomain at the same time.</summary>
public sealed class ApproveSchoolRequest
{
    public string? Subdomain { get; init; }
}

public sealed class RejectSchoolRequest
{
    public string Reason { get; init; } = string.Empty;
}

public sealed class SuspendSchoolRequest
{
    public string Reason { get; init; } = string.Empty;
}
