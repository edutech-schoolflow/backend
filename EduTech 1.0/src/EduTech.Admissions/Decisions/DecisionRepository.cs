using EduTech.Admissions.Domain;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Admissions.Decisions;

internal interface IDecisionRepository
{
    Task<Guid> RecordAsync(Guid applicationId, string outcome, string? conditions, string? notes, CancellationToken cancellationToken);
    Task<IReadOnlyList<Decision>> ListForApplicationAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<Decision?> GetLatestAsync(Guid applicationId, CancellationToken cancellationToken);
}

internal sealed class DecisionRow
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public string Outcome { get; init; } = "approved";
    public string? Conditions { get; init; }
    public string? Notes { get; init; }
    public Guid? DecidedBy { get; init; }
    public DateTime DecidedAt { get; init; }
}

internal sealed class DecisionRepository : TenantRepository, IDecisionRepository
{
    private const string Columns =
        "id AS Id, application_id AS ApplicationId, outcome AS Outcome, conditions AS Conditions, " +
        "notes AS Notes, decided_by AS DecidedBy, decided_at AS DecidedAt";

    public DecisionRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
    }

    public Task<Guid> RecordAsync(Guid applicationId, string outcome, string? conditions, string? notes,
        CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO decisions (application_id, school_id, outcome, conditions, notes)
            SELECT a.id, @SchoolId, @Outcome, @Conditions, @Notes
            FROM admission_applications a
            WHERE a.id = @ApplicationId AND a.school_id = @SchoolId
            RETURNING id
            """,
            TenantParameters(new { ApplicationId = applicationId, Outcome = outcome, Conditions = conditions, Notes = notes }),
            cancellationToken);
    }

    public async Task<IReadOnlyList<Decision>> ListForApplicationAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<DecisionRow> rows = await QueryAsync<DecisionRow>(
            $"SELECT {Columns} FROM decisions WHERE application_id = @ApplicationId AND school_id = @SchoolId ORDER BY decided_at DESC",
            TenantParameters(new { ApplicationId = applicationId }), cancellationToken);
        return rows.Select(Rehydrate).ToList();
    }

    public async Task<Decision?> GetLatestAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        DecisionRow? row = await QuerySingleOrDefaultAsync<DecisionRow>(
            $"SELECT {Columns} FROM decisions WHERE application_id = @ApplicationId AND school_id = @SchoolId ORDER BY decided_at DESC LIMIT 1",
            TenantParameters(new { ApplicationId = applicationId }), cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    internal static string OutcomeToDb(DecisionOutcome outcome) => outcome switch
    {
        DecisionOutcome.Conditional => "conditional",
        DecisionOutcome.Waitlisted => "waitlisted",
        DecisionOutcome.Rejected => "rejected",
        DecisionOutcome.Withdrawn => "withdrawn",
        _ => "approved"
    };

    private static Decision Rehydrate(DecisionRow r) => new(
        r.Id, r.ApplicationId,
        r.Outcome switch
        {
            "conditional" => DecisionOutcome.Conditional,
            "waitlisted" => DecisionOutcome.Waitlisted,
            "rejected" => DecisionOutcome.Rejected,
            "withdrawn" => DecisionOutcome.Withdrawn,
            _ => DecisionOutcome.Approved
        },
        r.Conditions, r.Notes, r.DecidedBy, r.DecidedAt);
}
