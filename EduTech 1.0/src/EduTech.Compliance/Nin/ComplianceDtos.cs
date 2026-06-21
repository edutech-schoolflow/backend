namespace EduTech.Compliance.Nin;

public sealed class SubmitNinRequest
{
    public string Nin { get; init; } = string.Empty;
}

/// <summary>Mirrors the frontend ComplianceStep (status: not_started | in_progress | pending | verified).</summary>
public sealed class ComplianceStepResponse
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Status { get; init; }
    public required bool Required { get; init; }
}

/// <summary>Mirrors the frontend ComplianceRecord (overallStatus: incomplete | pending | verified).</summary>
public sealed class ComplianceRecordResponse
{
    public required string ActorType { get; init; }   // teacher | parent
    public required string OverallStatus { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public required IReadOnlyList<ComplianceStepResponse> Steps { get; init; }
}
