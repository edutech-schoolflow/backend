namespace EduTech.Grades.ReportCards;

/// <summary>One band of the grading scale (mirrors the frontend GradeBoundary).</summary>
public sealed class GradeBoundaryDto
{
    public int MinScore { get; init; }
    public int MaxScore { get; init; }
    public string Grade { get; init; } = string.Empty;    // "A".."F" — school-defined free text
    public string Remark { get; init; } = string.Empty;   // "Excellent", ...
}

public sealed class SaveGradingScaleRequest
{
    public List<GradeBoundaryDto> Bands { get; init; } = new List<GradeBoundaryDto>();
}
