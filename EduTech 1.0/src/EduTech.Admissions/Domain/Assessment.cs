using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Domain;

public enum AssessmentType
{
    Exam,
    Interview,
    Observation,
    Portfolio,
    ExternalResult
}

public enum AssessmentStatus
{
    Scheduled,
    Completed,
    Cancelled
}

/// <summary>
/// Assessment (EDD-014) — a typed step a school uses to evaluate an applicant (exam / interview /
/// observation / portfolio / external result). Lifecycle: scheduled → (completed with a result |
/// cancelled). The result (outcome · score · notes) is recorded on completion.
/// </summary>
internal sealed class Assessment
{
    public Assessment(Guid id, Guid applicationId, AssessmentType type, DateTime? scheduledAt,
        AssessmentStatus status, string? outcome, decimal? score, string? resultNotes, DateTime? recordedAt,
        DateTime createdAt)
    {
        Id = id;
        ApplicationId = applicationId;
        Type = type;
        ScheduledAt = scheduledAt;
        Status = status;
        Outcome = outcome;
        Score = score;
        ResultNotes = resultNotes;
        RecordedAt = recordedAt;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid ApplicationId { get; }
    public AssessmentType Type { get; }
    public DateTime? ScheduledAt { get; private set; }
    public AssessmentStatus Status { get; private set; }
    public string? Outcome { get; private set; }
    public decimal? Score { get; private set; }
    public string? ResultNotes { get; private set; }
    public DateTime? RecordedAt { get; private set; }
    public DateTime CreatedAt { get; }

    public void Reschedule(DateTime? scheduledAt)
    {
        RequireScheduled("rescheduled");
        ScheduledAt = scheduledAt;
    }

    /// <summary>Records the outcome and completes the assessment.</summary>
    public void RecordResult(string outcome, decimal? score, string? notes, DateTime nowUtc)
    {
        RequireScheduled("recorded");
        if (string.IsNullOrWhiteSpace(outcome))
        {
            throw new AppErrorException("An assessment result needs an outcome.", 400, ErrorCodes.ValidationError);
        }

        Outcome = outcome.Trim();
        Score = score;
        ResultNotes = notes;
        RecordedAt = nowUtc;
        Status = AssessmentStatus.Completed;
    }

    public void Cancel()
    {
        RequireScheduled("cancelled");
        Status = AssessmentStatus.Cancelled;
    }

    private void RequireScheduled(string verb)
    {
        if (Status != AssessmentStatus.Scheduled)
        {
            throw new AppErrorException($"Only a scheduled assessment can be {verb}.", 409,
                ErrorCodes.Conflict, logReason: $"{verb} attempted on assessment in status {Status}.");
        }
    }
}
