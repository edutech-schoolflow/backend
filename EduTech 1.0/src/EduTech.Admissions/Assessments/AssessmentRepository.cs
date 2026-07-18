using EduTech.Admissions.Domain;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Admissions.Assessments;

internal interface IAssessmentRepository
{
    Task<Guid> ScheduleAsync(Guid applicationId, string type, DateTime? scheduledAt, CancellationToken cancellationToken);
    Task<Assessment?> GetAsync(Guid applicationId, Guid assessmentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Assessment>> ListForApplicationAsync(Guid applicationId, CancellationToken cancellationToken);
    Task SaveAsync(Assessment assessment, CancellationToken cancellationToken);
}

internal sealed class AssessmentRow
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public string Type { get; init; } = "exam";
    public DateTime? ScheduledAt { get; init; }
    public string Status { get; init; } = "scheduled";
    public string? Outcome { get; init; }
    public decimal? Score { get; init; }
    public string? ResultNotes { get; init; }
    public DateTime? RecordedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class AssessmentRepository : TenantRepository, IAssessmentRepository
{
    private const string Columns =
        "id AS Id, application_id AS ApplicationId, type AS Type, scheduled_at AS ScheduledAt, status, " +
        "outcome AS Outcome, score AS Score, result_notes AS ResultNotes, recorded_at AS RecordedAt, created_at AS CreatedAt";

    public AssessmentRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
    }

    public Task<Guid> ScheduleAsync(Guid applicationId, string type, DateTime? scheduledAt, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO assessments (application_id, school_id, type, scheduled_at, status)
            SELECT a.id, @SchoolId, @Type, @ScheduledAt, 'scheduled'
            FROM admission_applications a
            WHERE a.id = @ApplicationId AND a.school_id = @SchoolId
            RETURNING id
            """,
            TenantParameters(new { ApplicationId = applicationId, Type = type, ScheduledAt = scheduledAt }),
            cancellationToken);
    }

    public async Task<Assessment?> GetAsync(Guid applicationId, Guid assessmentId, CancellationToken cancellationToken)
    {
        AssessmentRow? row = await QuerySingleOrDefaultAsync<AssessmentRow>(
            $"SELECT {Columns} FROM assessments WHERE id = @Id AND application_id = @ApplicationId AND school_id = @SchoolId",
            TenantParameters(new { Id = assessmentId, ApplicationId = applicationId }), cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public async Task<IReadOnlyList<Assessment>> ListForApplicationAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<AssessmentRow> rows = await QueryAsync<AssessmentRow>(
            $"SELECT {Columns} FROM assessments WHERE application_id = @ApplicationId AND school_id = @SchoolId ORDER BY created_at",
            TenantParameters(new { ApplicationId = applicationId }), cancellationToken);
        return rows.Select(Rehydrate).ToList();
    }

    public Task SaveAsync(Assessment assessment, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE assessments
               SET scheduled_at = @ScheduledAt, status = @Status, outcome = @Outcome, score = @Score,
                   result_notes = @ResultNotes, recorded_at = @RecordedAt, updated_at = NOW()
             WHERE id = @Id AND school_id = @SchoolId
            """,
            TenantParameters(new
            {
                Id = assessment.Id, ScheduledAt = assessment.ScheduledAt, Status = StatusToDb(assessment.Status),
                Outcome = assessment.Outcome, Score = assessment.Score, ResultNotes = assessment.ResultNotes,
                RecordedAt = assessment.RecordedAt
            }),
            cancellationToken);
    }

    internal static string TypeToDb(AssessmentType type) => type switch
    {
        AssessmentType.Interview => "interview",
        AssessmentType.Observation => "observation",
        AssessmentType.Portfolio => "portfolio",
        AssessmentType.ExternalResult => "external_result",
        _ => "exam"
    };

    private static string StatusToDb(AssessmentStatus status) => status switch
    {
        AssessmentStatus.Completed => "completed",
        AssessmentStatus.Cancelled => "cancelled",
        _ => "scheduled"
    };

    private static Assessment Rehydrate(AssessmentRow r) => new(
        r.Id, r.ApplicationId,
        r.Type switch
        {
            "interview" => AssessmentType.Interview,
            "observation" => AssessmentType.Observation,
            "portfolio" => AssessmentType.Portfolio,
            "external_result" => AssessmentType.ExternalResult,
            _ => AssessmentType.Exam
        },
        r.ScheduledAt,
        r.Status switch
        {
            "completed" => AssessmentStatus.Completed,
            "cancelled" => AssessmentStatus.Cancelled,
            _ => AssessmentStatus.Scheduled
        },
        r.Outcome, r.Score, r.ResultNotes, r.RecordedAt, r.CreatedAt);
}
