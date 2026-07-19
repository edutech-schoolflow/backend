using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Domain;

public enum OfferStatus
{
    Issued,
    Accepted,
    Declined,
    Lapsed,
    Withdrawn
}

/// <summary>
/// Offer (EDD-014) — a first-class offer of a place: campus, class, academic year, fee plan,
/// scholarship, acceptance deadline, conditions. Issued from an approved/conditional Decision; then
/// accepted, declined, withdrawn (by the school), or lapsed (past its deadline). issued is the only
/// non-terminal state; the rest are terminal. Accepting is the trigger the platform reacts to.
/// </summary>
internal sealed class Offer
{
    public Offer(Guid id, Guid applicationId, Guid? decisionId, string? campus, Guid? classId,
        string? academicYear, string? feePlan, string? scholarship, string? conditions,
        DateTime? acceptanceDeadline, OfferStatus status, DateTime? respondedAt, DateTime createdAt)
    {
        Id = id;
        ApplicationId = applicationId;
        DecisionId = decisionId;
        Campus = campus;
        ClassId = classId;
        AcademicYear = academicYear;
        FeePlan = feePlan;
        Scholarship = scholarship;
        Conditions = conditions;
        AcceptanceDeadline = acceptanceDeadline;
        Status = status;
        RespondedAt = respondedAt;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid ApplicationId { get; }
    public Guid? DecisionId { get; }
    public string? Campus { get; }
    public Guid? ClassId { get; }
    public string? AcademicYear { get; }
    public string? FeePlan { get; }
    public string? Scholarship { get; }
    public string? Conditions { get; }
    public DateTime? AcceptanceDeadline { get; }
    public OfferStatus Status { get; private set; }
    public DateTime? RespondedAt { get; private set; }
    public DateTime CreatedAt { get; }

    public void Accept(DateTime nowUtc)
    {
        RequireIssued("accepted");
        Status = OfferStatus.Accepted;
        RespondedAt = nowUtc;
    }

    public void Decline(DateTime nowUtc)
    {
        RequireIssued("declined");
        Status = OfferStatus.Declined;
        RespondedAt = nowUtc;
    }

    /// <summary>The school withdraws an outstanding offer.</summary>
    public void Withdraw()
    {
        RequireIssued("withdrawn");
        Status = OfferStatus.Withdrawn;
    }

    /// <summary>The offer lapses at its acceptance deadline (system).</summary>
    public void Lapse()
    {
        RequireIssued("lapsed");
        Status = OfferStatus.Lapsed;
    }

    private void RequireIssued(string verb)
    {
        if (Status != OfferStatus.Issued)
        {
            throw new AppErrorException($"Only an outstanding offer can be {verb}.", 409,
                ErrorCodes.Conflict, logReason: $"{verb} attempted on offer in status {Status}.");
        }
    }
}
