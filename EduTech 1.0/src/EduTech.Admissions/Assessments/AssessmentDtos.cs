using EduTech.Admissions.Domain;

namespace EduTech.Admissions.Assessments;

public sealed class ScheduleAssessmentRequest
{
    public AssessmentType Type { get; init; }
    public DateTime? ScheduledAt { get; init; }
}

public sealed class RescheduleAssessmentRequest
{
    public DateTime? ScheduledAt { get; init; }
}

public sealed class RecordResultRequest
{
    public string Outcome { get; init; } = string.Empty;
    public decimal? Score { get; init; }
    public string? Notes { get; init; }
}

public sealed class AssessmentResponse
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public AssessmentType Type { get; init; }
    public DateTime? ScheduledAt { get; init; }
    public AssessmentStatus Status { get; init; }
    public string? Outcome { get; init; }
    public decimal? Score { get; init; }
    public string? ResultNotes { get; init; }
    public DateTime? RecordedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
