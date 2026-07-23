using EduTech.Admissions.Domain;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Admissions.Applications;

internal sealed class NewApplication
{
    public Guid CycleId { get; init; }
    public Guid? SourceInquiryId { get; init; }
    public string ProspectiveName { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public string? GuardianName { get; init; }
    public string GuardianPhone { get; init; } = string.Empty;
    public string? PreferredClass { get; init; }
}

internal interface IApplicationRepository
{
    Task<Guid> CreateDraftAsync(NewApplication application, CancellationToken cancellationToken);
    Task<Application?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Application>> ListAsync(Guid? cycleId, string? status, CancellationToken cancellationToken);
    Task SaveAsync(Application application, CancellationToken cancellationToken);
}

internal sealed class ApplicationRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid CycleId { get; init; }
    public Guid? ChildProfileId { get; init; }
    public Guid? SourceInquiryId { get; init; }
    public string ProspectiveName { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public string? GuardianName { get; init; }
    public string GuardianPhone { get; init; } = string.Empty;
    public string? PreferredClass { get; init; }
    public string Status { get; init; } = "draft";
    public DateTime? SubmittedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class ApplicationRepository : TenantRepository, IApplicationRepository
{
    private const string Columns =
        "id AS Id, school_id AS OrganizationId, cycle_id AS CycleId, child_profile_id AS ChildProfileId, " +
        "source_inquiry_id AS SourceInquiryId, prospective_name AS ProspectiveName, date_of_birth AS DateOfBirth, " +
        "gender AS Gender, guardian_name AS GuardianName, guardian_phone AS GuardianPhone, " +
        "preferred_class AS PreferredClass, status, submitted_at AS SubmittedAt, created_at AS CreatedAt";

    public ApplicationRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
    }

    public Task<Guid> CreateDraftAsync(NewApplication a, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO admission_applications
                (school_id, cycle_id, source_inquiry_id, prospective_name, date_of_birth, gender,
                 guardian_name, guardian_phone, preferred_class, status)
            VALUES (@SchoolId, @CycleId, @SourceInquiryId, @ProspectiveName, @DateOfBirth, @Gender,
                    @GuardianName, @GuardianPhone, @PreferredClass, 'draft')
            RETURNING id
            """,
            TenantParameters(new
            {
                a.CycleId, a.SourceInquiryId, a.ProspectiveName, a.DateOfBirth, a.Gender,
                a.GuardianName, a.GuardianPhone, a.PreferredClass
            }),
            cancellationToken);
    }

    public async Task<Application?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        ApplicationRow? row = await QuerySingleOrDefaultAsync<ApplicationRow>(
            $"SELECT {Columns} FROM admission_applications WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = id }), cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public async Task<IReadOnlyList<Application>> ListAsync(Guid? cycleId, string? status,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationRow> rows = await QueryAsync<ApplicationRow>(
            $"""
            SELECT {Columns} FROM admission_applications
            WHERE school_id = @SchoolId
              AND (@CycleId IS NULL OR cycle_id = @CycleId)
              AND (@Status IS NULL OR status = @Status)
            ORDER BY created_at DESC
            """,
            TenantParameters(new { CycleId = cycleId, Status = status }), cancellationToken);
        return rows.Select(Rehydrate).ToList();
    }

    public Task SaveAsync(Application application, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE admission_applications
               SET status = @Status, submitted_at = @SubmittedAt, updated_at = NOW()
             WHERE id = @Id AND school_id = @SchoolId
            """,
            TenantParameters(new { Id = application.Id, Status = ToDb(application.Status),
                SubmittedAt = application.SubmittedAt }),
            cancellationToken);
    }

    private static string ToDb(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Submitted => "submitted",
        ApplicationStatus.InReview => "in_review",
        ApplicationStatus.Decided => "decided",
        ApplicationStatus.Offered => "offered",
        ApplicationStatus.Accepted => "accepted",
        ApplicationStatus.Enrolled => "enrolled",
        ApplicationStatus.Withdrawn => "withdrawn",
        _ => "draft"
    };

    private static Application Rehydrate(ApplicationRow r) => new(
        r.Id, r.OrganizationId, r.CycleId, r.ChildProfileId, r.SourceInquiryId, r.ProspectiveName,
        r.DateOfBirth, r.Gender, r.GuardianName, r.GuardianPhone, r.PreferredClass,
        r.Status switch
        {
            "submitted" => ApplicationStatus.Submitted,
            "in_review" => ApplicationStatus.InReview,
            "decided" => ApplicationStatus.Decided,
            "offered" => ApplicationStatus.Offered,
            "accepted" => ApplicationStatus.Accepted,
            "enrolled" => ApplicationStatus.Enrolled,
            "withdrawn" => ApplicationStatus.Withdrawn,
            _ => ApplicationStatus.Draft
        },
        r.SubmittedAt, r.CreatedAt);
}
