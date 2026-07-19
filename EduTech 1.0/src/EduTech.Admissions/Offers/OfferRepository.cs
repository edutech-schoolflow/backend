using EduTech.Admissions.Domain;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Admissions.Offers;

internal sealed class NewOffer
{
    public Guid? DecisionId { get; init; }
    public string? Campus { get; init; }
    public Guid? ClassId { get; init; }
    public string? AcademicYear { get; init; }
    public string? FeePlan { get; init; }
    public string? Scholarship { get; init; }
    public string? Conditions { get; init; }
    public DateTime? AcceptanceDeadline { get; init; }
}

internal interface IOfferRepository
{
    Task<Guid> IssueAsync(Guid applicationId, NewOffer offer, CancellationToken cancellationToken);
    Task<Offer?> GetAsync(Guid applicationId, Guid offerId, CancellationToken cancellationToken);
    Task<Offer?> GetActiveAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Offer>> ListForApplicationAsync(Guid applicationId, CancellationToken cancellationToken);
    Task SaveAsync(Offer offer, CancellationToken cancellationToken);
}

internal sealed class OfferRow
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid? DecisionId { get; init; }
    public string? Campus { get; init; }
    public Guid? ClassId { get; init; }
    public string? AcademicYear { get; init; }
    public string? FeePlan { get; init; }
    public string? Scholarship { get; init; }
    public string? Conditions { get; init; }
    public DateTime? AcceptanceDeadline { get; init; }
    public string Status { get; init; } = "issued";
    public DateTime? RespondedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class OfferRepository : TenantRepository, IOfferRepository
{
    private const string Columns =
        "id AS Id, application_id AS ApplicationId, decision_id AS DecisionId, campus AS Campus, class_id AS ClassId, " +
        "academic_year AS AcademicYear, fee_plan AS FeePlan, scholarship AS Scholarship, conditions AS Conditions, " +
        "acceptance_deadline AS AcceptanceDeadline, status, responded_at AS RespondedAt, created_at AS CreatedAt";

    public OfferRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
    }

    public Task<Guid> IssueAsync(Guid applicationId, NewOffer o, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO offers (application_id, school_id, decision_id, campus, class_id, academic_year,
                                fee_plan, scholarship, conditions, acceptance_deadline, status)
            SELECT a.id, @SchoolId, @DecisionId, @Campus, @ClassId, @AcademicYear, @FeePlan, @Scholarship,
                   @Conditions, @AcceptanceDeadline, 'issued'
            FROM admission_applications a
            WHERE a.id = @ApplicationId AND a.school_id = @SchoolId
            RETURNING id
            """,
            TenantParameters(new
            {
                ApplicationId = applicationId, o.DecisionId, o.Campus, o.ClassId, o.AcademicYear,
                o.FeePlan, o.Scholarship, o.Conditions, o.AcceptanceDeadline
            }),
            cancellationToken);
    }

    public async Task<Offer?> GetAsync(Guid applicationId, Guid offerId, CancellationToken cancellationToken)
    {
        OfferRow? row = await QuerySingleOrDefaultAsync<OfferRow>(
            $"SELECT {Columns} FROM offers WHERE id = @Id AND application_id = @ApplicationId AND school_id = @SchoolId",
            TenantParameters(new { Id = offerId, ApplicationId = applicationId }), cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public async Task<Offer?> GetActiveAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        OfferRow? row = await QuerySingleOrDefaultAsync<OfferRow>(
            $"SELECT {Columns} FROM offers WHERE application_id = @ApplicationId AND school_id = @SchoolId AND status = 'issued'",
            TenantParameters(new { ApplicationId = applicationId }), cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public async Task<IReadOnlyList<Offer>> ListForApplicationAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<OfferRow> rows = await QueryAsync<OfferRow>(
            $"SELECT {Columns} FROM offers WHERE application_id = @ApplicationId AND school_id = @SchoolId ORDER BY created_at DESC",
            TenantParameters(new { ApplicationId = applicationId }), cancellationToken);
        return rows.Select(Rehydrate).ToList();
    }

    public Task SaveAsync(Offer offer, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE offers SET status = @Status, responded_at = @RespondedAt, updated_at = NOW() WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = offer.Id, Status = ToDb(offer.Status), RespondedAt = offer.RespondedAt }),
            cancellationToken);
    }

    private static string ToDb(OfferStatus status) => status switch
    {
        OfferStatus.Accepted => "accepted",
        OfferStatus.Declined => "declined",
        OfferStatus.Lapsed => "lapsed",
        OfferStatus.Withdrawn => "withdrawn",
        _ => "issued"
    };

    private static Offer Rehydrate(OfferRow r) => new(
        r.Id, r.ApplicationId, r.DecisionId, r.Campus, r.ClassId, r.AcademicYear, r.FeePlan, r.Scholarship,
        r.Conditions, r.AcceptanceDeadline,
        r.Status switch
        {
            "accepted" => OfferStatus.Accepted,
            "declined" => OfferStatus.Declined,
            "lapsed" => OfferStatus.Lapsed,
            "withdrawn" => OfferStatus.Withdrawn,
            _ => OfferStatus.Issued
        },
        r.RespondedAt, r.CreatedAt);
}
