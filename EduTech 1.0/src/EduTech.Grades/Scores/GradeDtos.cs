using EduTech.Shared.Constants;

namespace EduTech.Grades.Scores;

/// <summary>An arm the caller may enter grades for (class-teacher arm for primary, subject arm for secondary).</summary>
public sealed class GradeableArmResponse
{
    public required Guid ArmId { get; init; }
    public required string ArmName { get; init; }
    public required Guid ClassId { get; init; }
    public required string ClassName { get; init; }
    public required ClassLevel Level { get; init; }
}

/// <summary>A student row in the score grid, pre-filled with any existing score for this assessment.</summary>
public sealed class GradeEntryStudent
{
    public required Guid StudentId { get; init; }
    public required string StudentName { get; init; }
    public string? AdmissionNumber { get; init; }
    public decimal? Score { get; init; }   // null = not yet entered
}

/// <summary>The (arm, subject, term, assessment) record + roster, for the entry grid.</summary>
public sealed class GradeRecordResponse
{
    public Guid? RecordId { get; init; }   // null if not started yet
    public required Guid ArmId { get; init; }
    public required string ArmName { get; init; }
    public required Guid SubjectId { get; init; }
    public required string SubjectName { get; init; }
    public required Guid TermId { get; init; }
    public required AssessmentType AssessmentType { get; init; }
    public required int MaxScore { get; init; }
    public required GradeStatus Status { get; init; }
    public required IReadOnlyList<GradeEntryStudent> Students { get; init; }
}

public sealed class GradeEntryInput
{
    public Guid StudentId { get; init; }
    public decimal? Score { get; init; }   // null entries are skipped (not yet scored)
}

public sealed class SubmitGradesRequest
{
    public Guid ArmId { get; init; }
    public Guid SubjectId { get; init; }
    public Guid TermId { get; init; }
    public AssessmentType? AssessmentType { get; init; }   // null => missing/invalid
    public List<GradeEntryInput> Entries { get; init; } = new List<GradeEntryInput>();
}

public sealed class GradeRecordSummaryResponse
{
    public required Guid Id { get; init; }
    public required Guid ArmId { get; init; }
    public required Guid SubjectId { get; init; }
    public required Guid TermId { get; init; }
    public required AssessmentType AssessmentType { get; init; }
    public required int MaxScore { get; init; }
    public required GradeStatus Status { get; init; }
    public required int EnteredCount { get; init; }
    public required int TotalCount { get; init; }
    public required DateTime SubmittedAt { get; init; }
}

public sealed class PublishAllRequest
{
    public Guid TermId { get; init; }
    public Guid? ArmId { get; init; }   // null => the whole term
}

// ---- overview / grades board ----

public sealed class GradeSummaryRowResponse
{
    public required Guid RecordId { get; init; }
    public required Guid ArmId { get; init; }
    public required string ArmName { get; init; }
    public required Guid SubjectId { get; init; }
    public required string SubjectName { get; init; }
    public required AssessmentType AssessmentType { get; init; }
    public required int MaxScore { get; init; }
    public required GradeStatus Status { get; init; }
    public required decimal AverageScore { get; init; }
    public required int PassCount { get; init; }
    public required int FailCount { get; init; }
    public required int TotalCount { get; init; }
    public required DateTime SubmittedAt { get; init; }
}

public sealed class GradesOverviewResponse
{
    public required Guid TermId { get; init; }
    public required int TotalSubmitted { get; init; }
    public required int TotalPublished { get; init; }
    public required IReadOnlyList<GradeSummaryRowResponse> Rows { get; init; }
}
