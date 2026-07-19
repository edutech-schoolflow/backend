using EduTech.Admissions.Domain;

namespace EduTech.Admissions.Decisions;

public sealed class RecordDecisionRequest
{
    public DecisionOutcome Outcome { get; init; }
    public string? Conditions { get; init; }
    public string? Notes { get; init; }
}

public sealed class DecisionResponse
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public DecisionOutcome Outcome { get; init; }
    public string? Conditions { get; init; }
    public string? Notes { get; init; }
    public DateTime DecidedAt { get; init; }
}
