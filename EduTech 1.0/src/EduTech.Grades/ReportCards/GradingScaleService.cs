using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Grades.ReportCards;

public interface IGradingScaleService
{
    /// <summary>The school's saved bands, or the default 5-band scale if it hasn't customised one.</summary>
    Task<IReadOnlyList<GradeBoundaryDto>> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(SaveGradingScaleRequest request, CancellationToken cancellationToken);
}

internal sealed class GradingScaleService : IGradingScaleService
{
    private readonly IGradingScaleRepository _repository;

    public GradingScaleService(IGradingScaleRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<GradeBoundaryDto>> GetAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<GradeBoundaryRow> rows = await _repository.GetAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return GradingScale.Defaults;
        }

        return rows.Select(r => new GradeBoundaryDto
        {
            MinScore = r.MinScore, MaxScore = r.MaxScore, Grade = r.Grade, Remark = r.Remark
        }).ToList();
    }

    public async Task SaveAsync(SaveGradingScaleRequest request, CancellationToken cancellationToken)
    {
        List<GradeBoundaryDto> bands = request.Bands ?? new List<GradeBoundaryDto>();
        if (bands.Count == 0)
        {
            throw new AppErrorException("Add at least one grade band.", 400, ErrorCodes.ValidationError);
        }

        List<GradeBoundaryDto> cleaned = new List<GradeBoundaryDto>(bands.Count);
        HashSet<string> labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GradeBoundaryDto band in bands)
        {
            string grade = (band.Grade ?? string.Empty).Trim();
            string remark = (band.Remark ?? string.Empty).Trim();

            if (grade.Length == 0)
            {
                throw new AppErrorException("Each band needs a grade label.", 400, ErrorCodes.ValidationError);
            }

            if (!labels.Add(grade))
            {
                throw new AppErrorException($"Grade label '{grade}' is used more than once.", 400, ErrorCodes.ValidationError);
            }

            if (band.MinScore < 0 || band.MaxScore > 100 || band.MinScore > band.MaxScore)
            {
                throw new AppErrorException("Each band must have 0 ≤ min ≤ max ≤ 100.", 400, ErrorCodes.ValidationError);
            }

            cleaned.Add(new GradeBoundaryDto
            {
                MinScore = band.MinScore, MaxScore = band.MaxScore, Grade = grade, Remark = remark
            });
        }

        // Every total from 0 to 100 must resolve to exactly one band: bands must tile the scale with no
        // gaps (a gapped total would print as "-") and no overlaps (an overlapped total is ambiguous).
        cleaned.Sort((a, b) => a.MinScore.CompareTo(b.MinScore));
        if (cleaned[0].MinScore != 0)
        {
            throw new AppErrorException("The lowest band must start at 0.", 400, ErrorCodes.ValidationError);
        }

        if (cleaned[^1].MaxScore != 100)
        {
            throw new AppErrorException("The highest band must end at 100.", 400, ErrorCodes.ValidationError);
        }

        for (int i = 1; i < cleaned.Count; i++)
        {
            if (cleaned[i].MinScore != cleaned[i - 1].MaxScore + 1)
            {
                throw new AppErrorException(
                    $"Bands must be contiguous: '{cleaned[i].Grade}' must start at {cleaned[i - 1].MaxScore + 1} " +
                    $"(right after '{cleaned[i - 1].Grade}').",
                    400, ErrorCodes.ValidationError);
            }
        }

        await _repository.ReplaceAsync(cleaned, cancellationToken);
    }
}
