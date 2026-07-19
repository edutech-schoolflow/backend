using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Domain;

public enum ApplicationStatus
{
    Draft,
    Submitted,
    InReview,
    Decided,
    Offered,
    Accepted,
    Enrolled,
    Withdrawn
}

/// <summary>
/// Application (EDD-014) — a prospective learner applying to an AdmissionCycle. The applicant is a
/// Child Profile (EDD-007); until the platform exposes a Child-Profile contract, the applicant's
/// details live inline and <see cref="ChildProfileId"/> is linked later. Slice 3 owns
/// draft → submitted → withdrawn; downstream states (in_review … enrolled) are driven by later slices.
/// </summary>
internal sealed class Application
{
    public Application(Guid id, Guid organizationId, Guid cycleId, Guid? childProfileId, Guid? sourceInquiryId,
        string prospectiveName, DateOnly? dateOfBirth, string? gender, string? guardianName, string guardianPhone,
        string? preferredClass, ApplicationStatus status, DateTime? submittedAt, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(prospectiveName))
        {
            throw new AppErrorException("An application needs the applicant's name.", 400,
                ErrorCodes.ValidationError, logReason: "Application created without a prospective name.");
        }
        if (string.IsNullOrWhiteSpace(guardianPhone))
        {
            throw new AppErrorException("An application needs a guardian contact phone.", 400, ErrorCodes.ValidationError);
        }

        Id = id;
        OrganizationId = organizationId;
        CycleId = cycleId;
        ChildProfileId = childProfileId;
        SourceInquiryId = sourceInquiryId;
        ProspectiveName = prospectiveName.Trim();
        DateOfBirth = dateOfBirth;
        Gender = gender;
        GuardianName = string.IsNullOrWhiteSpace(guardianName) ? null : guardianName!.Trim();
        GuardianPhone = guardianPhone.Trim();
        PreferredClass = preferredClass;
        Status = status;
        SubmittedAt = submittedAt;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public Guid CycleId { get; }
    public Guid? ChildProfileId { get; }
    public Guid? SourceInquiryId { get; }
    public string ProspectiveName { get; }
    public DateOnly? DateOfBirth { get; }
    public string? Gender { get; }
    public string? GuardianName { get; }
    public string GuardianPhone { get; }
    public string? PreferredClass { get; }
    public ApplicationStatus Status { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime CreatedAt { get; }

    /// <summary>Submits a draft for review. Publishes ApplicationSubmitted (in the service).</summary>
    public void Submit(DateTime nowUtc)
    {
        if (Status != ApplicationStatus.Draft)
        {
            throw new AppErrorException("Only a draft application can be submitted.", 409,
                ErrorCodes.Conflict, logReason: $"Submit attempted on application in status {Status}.");
        }

        Status = ApplicationStatus.Submitted;
        SubmittedAt = nowUtc;
    }

    /// <summary>Records that a decision has been made (from submitted / in-review). Slice 6.</summary>
    public void MarkDecided()
    {
        if (Status is not (ApplicationStatus.Submitted or ApplicationStatus.InReview))
        {
            throw new AppErrorException("Only a submitted application can be decided.", 409,
                ErrorCodes.Conflict, logReason: $"MarkDecided attempted on application in status {Status}.");
        }

        Status = ApplicationStatus.Decided;
    }

    /// <summary>An offer was issued (from a decided application). Slice 7.</summary>
    public void MarkOffered()
    {
        if (Status != ApplicationStatus.Decided)
        {
            throw new AppErrorException("Only a decided application can be offered a place.", 409,
                ErrorCodes.Conflict, logReason: $"MarkOffered attempted on application in status {Status}.");
        }

        Status = ApplicationStatus.Offered;
    }

    /// <summary>The family accepted the offer. Slice 7 (enrollment finalizes in Slice 8).</summary>
    public void MarkAccepted()
    {
        if (Status != ApplicationStatus.Offered)
        {
            throw new AppErrorException("Only an offered application can be accepted.", 409,
                ErrorCodes.Conflict, logReason: $"MarkAccepted attempted on application in status {Status}.");
        }

        Status = ApplicationStatus.Accepted;
    }

    /// <summary>Withdraws the application (by the family or the school). Terminal.</summary>
    public void Withdraw()
    {
        if (Status is ApplicationStatus.Enrolled or ApplicationStatus.Withdrawn)
        {
            throw new AppErrorException("This application can no longer be withdrawn.", 409, ErrorCodes.Conflict);
        }

        Status = ApplicationStatus.Withdrawn;
    }
}
