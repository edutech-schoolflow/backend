using EduTech.Admissions.Domain;

namespace EduTech.Admissions.Enrollments;

public sealed class CancelEnrollmentRequest
{
    public string Reason { get; init; } = string.Empty;
}

public sealed class EnrollmentResponse
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid? OfferId { get; init; }
    public Guid? ChildProfileId { get; init; }
    public EnrollmentStatus Status { get; init; }
    public string? CancelledReason { get; init; }
    public DateTime EnrolledAt { get; init; }
}
