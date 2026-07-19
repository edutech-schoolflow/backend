using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Domain;

public enum EnrollmentStatus
{
    Active,
    Cancelled
}

/// <summary>
/// Enrollment (EDD-014) — confirms an accepted offer became a real place in the organization. It is
/// **not** the Student: the Student aggregate (owned by the Students module) is created from the
/// StudentEnrolled event this raises. An enrollment can be cancelled (withdrawal before term).
/// </summary>
internal sealed class Enrollment
{
    public Enrollment(Guid id, Guid applicationId, Guid? offerId, Guid? childProfileId, EnrollmentStatus status,
        string? cancelledReason, DateTime enrolledAt)
    {
        Id = id;
        ApplicationId = applicationId;
        OfferId = offerId;
        ChildProfileId = childProfileId;
        Status = status;
        CancelledReason = cancelledReason;
        EnrolledAt = enrolledAt;
    }

    public Guid Id { get; }
    public Guid ApplicationId { get; }
    public Guid? OfferId { get; }
    public Guid? ChildProfileId { get; }
    public EnrollmentStatus Status { get; private set; }
    public string? CancelledReason { get; private set; }
    public DateTime EnrolledAt { get; }

    public void Cancel(string reason)
    {
        if (Status == EnrollmentStatus.Cancelled)
        {
            throw new AppErrorException("This enrollment is already cancelled.", 409, ErrorCodes.Conflict);
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new AppErrorException("Give a reason for cancelling the enrollment.", 400, ErrorCodes.ValidationError);
        }

        Status = EnrollmentStatus.Cancelled;
        CancelledReason = reason.Trim();
    }
}
