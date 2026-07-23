using EduTech.Admissions.Domain;

namespace EduTech.Admissions.Applications;

public sealed class CreateApplicationRequest
{
    public Guid CycleId { get; init; }
    public string ProspectiveName { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public string? GuardianName { get; init; }
    public string GuardianPhone { get; init; } = string.Empty;
    public string? PreferredClass { get; init; }
}

public sealed class ApplicationResponse
{
    public Guid Id { get; init; }
    public Guid CycleId { get; init; }
    public Guid? ChildProfileId { get; init; }
    public Guid? SourceInquiryId { get; init; }
    public string ProspectiveName { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public string? GuardianName { get; init; }
    public string GuardianPhone { get; init; } = string.Empty;
    public string? PreferredClass { get; init; }
    public ApplicationStatus Status { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
