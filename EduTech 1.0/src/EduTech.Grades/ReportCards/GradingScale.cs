namespace EduTech.Grades.ReportCards;

/// <summary>
/// The default grading scale (used when a school hasn't customised one) and the helper that maps a
/// total score to its band. Schools can override the bands via the grading-scale endpoints.
/// </summary>
internal static class GradingScale
{
    public static IReadOnlyList<GradeBoundaryDto> Defaults { get; } = new[]
    {
        new GradeBoundaryDto { MinScore = 70, MaxScore = 100, Grade = "A", Remark = "Excellent" },
        new GradeBoundaryDto { MinScore = 60, MaxScore = 69,  Grade = "B", Remark = "Very Good" },
        new GradeBoundaryDto { MinScore = 50, MaxScore = 59,  Grade = "C", Remark = "Good" },
        new GradeBoundaryDto { MinScore = 40, MaxScore = 49,  Grade = "D", Remark = "Fair" },
        new GradeBoundaryDto { MinScore = 0,  MaxScore = 39,  Grade = "F", Remark = "Fail" }
    };

    /// <summary>The grade + remark whose band contains <paramref name="total"/>; ("-", "") if none match.</summary>
    public static (string Grade, string Remark) Resolve(decimal total, IReadOnlyList<GradeBoundaryDto> bands)
    {
        foreach (GradeBoundaryDto band in bands)
        {
            if (total >= band.MinScore && total <= band.MaxScore)
            {
                return (band.Grade, band.Remark);
            }
        }

        return ("-", string.Empty);
    }
}
