using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Domain;

public enum DecisionOutcome
{
    Approved,
    Conditional,
    Waitlisted,
    Rejected,
    Withdrawn
}

/// <summary>
/// Decision (EDD-014) — an admissions outcome on an application: approved / conditional / waitlisted /
/// rejected / withdrawn. Append-only (the history of decisions; the latest is current). A conditional
/// decision must state its conditions. Only an approved/conditional decision can later produce an Offer.
/// </summary>
internal sealed class Decision
{
    public Decision(Guid id, Guid applicationId, DecisionOutcome outcome, string? conditions, string? notes,
        Guid? decidedBy, DateTime decidedAt)
    {
        if (outcome == DecisionOutcome.Conditional && string.IsNullOrWhiteSpace(conditions))
        {
            throw new AppErrorException("A conditional decision must state its conditions.", 400,
                ErrorCodes.ValidationError);
        }

        Id = id;
        ApplicationId = applicationId;
        Outcome = outcome;
        Conditions = string.IsNullOrWhiteSpace(conditions) ? null : conditions!.Trim();
        Notes = notes;
        DecidedBy = decidedBy;
        DecidedAt = decidedAt;
    }

    public Guid Id { get; }
    public Guid ApplicationId { get; }
    public DecisionOutcome Outcome { get; }
    public string? Conditions { get; }
    public string? Notes { get; }
    public Guid? DecidedBy { get; }
    public DateTime DecidedAt { get; }

    /// <summary>An approved or conditional decision may proceed to an Offer (Slice 7).</summary>
    public bool CanProduceOffer => Outcome is DecisionOutcome.Approved or DecisionOutcome.Conditional;
}
