using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Grades.ReportCards;

internal interface IGradingScaleRepository
{
    Task<IReadOnlyList<GradeBoundaryRow>> GetAsync(CancellationToken cancellationToken);
    Task ReplaceAsync(IReadOnlyList<GradeBoundaryDto> bands, CancellationToken cancellationToken);
}

internal sealed class GradeBoundaryRow
{
    public int MinScore { get; init; }
    public int MaxScore { get; init; }
    public string Grade { get; init; } = string.Empty;
    public string Remark { get; init; } = string.Empty;
}

internal sealed class GradingScaleRepository : TenantRepository, IGradingScaleRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GradingScaleRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<IReadOnlyList<GradeBoundaryRow>> GetAsync(CancellationToken cancellationToken)
    {
        return QueryAsync<GradeBoundaryRow>(
            """
            SELECT min_score AS MinScore, max_score AS MaxScore, grade, remark
            FROM grade_boundaries WHERE school_id = @SchoolId
            ORDER BY sort_order, min_score DESC
            """,
            TenantParameters(), cancellationToken);
    }

    public async Task ReplaceAsync(IReadOnlyList<GradeBoundaryDto> bands, CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            "DELETE FROM grade_boundaries WHERE school_id = @SchoolId",
            TenantParameters(), cancellationToken, transaction.Transaction);

        for (int i = 0; i < bands.Count; i++)
        {
            GradeBoundaryDto band = bands[i];
            await ExecuteAsync(
                """
                INSERT INTO grade_boundaries (school_id, min_score, max_score, grade, remark, sort_order)
                VALUES (@SchoolId, @Min, @Max, @Grade, @Remark, @Order)
                """,
                TenantParameters(new { Min = band.MinScore, Max = band.MaxScore, Grade = band.Grade, Remark = band.Remark, Order = i }),
                cancellationToken, transaction.Transaction);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
