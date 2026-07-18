using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Domain;

public enum InquiryStatus
{
    New,
    Contacted,
    VisitBooked,
    Converted,
    Closed
}

/// <summary>
/// Inquiry (EDD-014) — pre-application interest in a school. Pre-identity: an inquirer has no account
/// yet, so it holds raw contact details. Lifecycle: new → contacted → visit_booked → (converted |
/// closed); converted/closed are terminal. Converting produces an Application (Slice 3).
/// </summary>
internal sealed class Inquiry
{
    public Inquiry(Guid id, Guid organizationId, Guid? cycleId, string prospectiveName, string? guardianName,
        string guardianPhone, string? notes, DateTime? visitAt, InquiryStatus status,
        Guid? convertedApplicationId, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(prospectiveName))
        {
            throw new AppErrorException("An inquiry needs the prospective learner's name.", 400,
                ErrorCodes.ValidationError, logReason: "Inquiry created without a prospective name.");
        }
        if (string.IsNullOrWhiteSpace(guardianPhone))
        {
            throw new AppErrorException("An inquiry needs a contact phone.", 400, ErrorCodes.ValidationError);
        }

        Id = id;
        OrganizationId = organizationId;
        CycleId = cycleId;
        ProspectiveName = prospectiveName.Trim();
        GuardianName = string.IsNullOrWhiteSpace(guardianName) ? null : guardianName.Trim();
        GuardianPhone = guardianPhone.Trim();
        Notes = notes;
        VisitAt = visitAt;
        Status = status;
        ConvertedApplicationId = convertedApplicationId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public Guid? CycleId { get; }
    public string ProspectiveName { get; }
    public string? GuardianName { get; }
    public string GuardianPhone { get; }
    public string? Notes { get; }
    public DateTime? VisitAt { get; private set; }
    public InquiryStatus Status { get; private set; }
    public Guid? ConvertedApplicationId { get; private set; }
    public DateTime CreatedAt { get; }

    public void MarkContacted()
    {
        RequireOpen();
        Status = InquiryStatus.Contacted;
    }

    public void BookVisit(DateTime? visitAt)
    {
        RequireOpen();
        VisitAt = visitAt;
        Status = InquiryStatus.VisitBooked;
    }

    /// <summary>Converts the inquiry into an application (Slice 3 supplies the application id). Terminal.</summary>
    public void Convert(Guid applicationId)
    {
        RequireOpen();
        ConvertedApplicationId = applicationId;
        Status = InquiryStatus.Converted;
    }

    public void Close() => Status = InquiryStatus.Closed;

    private void RequireOpen()
    {
        if (Status is InquiryStatus.Converted or InquiryStatus.Closed)
        {
            throw new AppErrorException("This inquiry is already closed.", 409, ErrorCodes.Conflict,
                logReason: "Mutation attempted on a terminal inquiry.");
        }
    }
}
