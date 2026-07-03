using EduTech.Shared.Constants;

namespace EduTech.Grades.ReportCards;

/// <summary>One subject's line on a report card (totals + grade computed; positions intentionally omitted).</summary>
public sealed class SubjectGradeResponse
{
    public required Guid SubjectId { get; init; }
    public required string SubjectName { get; init; }
    public decimal? Ca1 { get; init; }
    public decimal? Ca2 { get; init; }
    public decimal? Exam { get; init; }
    public decimal? Total { get; init; }     // ca1 + ca2 + exam (null if nothing entered yet)
    public string? Grade { get; init; }       // letter via the school's scale
    public string? Remark { get; init; }
}

public sealed class BehavioralRatingDto
{
    public BehavioralTrait Trait { get; init; }
    public int Score { get; init; }           // 1..5
}

public sealed class SaveReportMetaRequest
{
    public string? TeacherComment { get; init; }
    public string? PrincipalComment { get; init; }
    public DateOnly? NextTermResumption { get; init; }
    public List<BehavioralRatingDto> BehavioralRatings { get; init; } = new List<BehavioralRatingDto>();
}

public sealed class PublishArmReportsRequest
{
    public Guid ArmId { get; init; }
    public Guid TermId { get; init; }
}

/// <summary>A row on the school's report-card list for an arm + term.</summary>
public sealed class ReportSummaryResponse
{
    public required Guid StudentId { get; init; }
    public required string StudentName { get; init; }
    public string? AdmissionNumber { get; init; }
    public decimal? OverallAverage { get; init; }
    public required GradeStatus Status { get; init; }
}

/// <summary>A full report card for a (student, term) — mirrors the frontend Report (minus positions).</summary>
public sealed class ReportCardResponse
{
    public required Guid StudentId { get; init; }
    public required string StudentName { get; init; }
    public string? AdmissionNumber { get; init; }
    public required string ClassName { get; init; }
    public required string ArmName { get; init; }
    public required Guid TermId { get; init; }
    public required Term Term { get; init; }
    public required string AcademicYear { get; init; }

    public required IReadOnlyList<SubjectGradeResponse> Grades { get; init; }
    public decimal? OverallAverage { get; init; }

    public string? TeacherComment { get; init; }
    public string? PrincipalComment { get; init; }
    public required IReadOnlyList<BehavioralRatingDto> BehavioralRatings { get; init; }

    public required int AttendanceDays { get; init; }
    public required int PresentDays { get; init; }
    public required int AbsentDays { get; init; }
    public required int LateDays { get; init; }

    public DateOnly? NextTermResumption { get; init; }
    public required GradeStatus Status { get; init; }   // draft | published (reuses GradeStatus)
    public DateTime? PublishedAt { get; init; }
}
