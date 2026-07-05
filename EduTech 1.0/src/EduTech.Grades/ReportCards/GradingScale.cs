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

    /// <summary>
    /// The band for <paramref name="total"/>: the highest band whose MinScore ≤ total. Bands are stored
    /// with integer edges but totals are decimal (half-marks), so a total in the crack between two bands
    /// (e.g. 59.5 against …-59 | 60-…) belongs to the band below it, never to no band. ("-", "") only
    /// when the total is below every band.
    /// </summary>
    public static (string Grade, string Remark) Resolve(decimal total, IReadOnlyList<GradeBoundaryDto> bands)
    {
        GradeBoundaryDto? band = bands
            .Where(b => total >= b.MinScore)
            .OrderByDescending(b => b.MinScore)
            .FirstOrDefault();

        return band is null ? ("-", string.Empty) : (band.Grade, band.Remark);
    }
}
