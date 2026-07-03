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
        foreach (GradeBoundaryDto band in bands)
        {
            string grade = (band.Grade ?? string.Empty).Trim();
            string remark = (band.Remark ?? string.Empty).Trim();

            if (grade.Length == 0)
            {
                throw new AppErrorException("Each band needs a grade label.", 400, ErrorCodes.ValidationError);
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

        await _repository.ReplaceAsync(cleaned, cancellationToken);
    }
}
