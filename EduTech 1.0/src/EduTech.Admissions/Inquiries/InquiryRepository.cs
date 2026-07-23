using EduTech.Admissions.Domain;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Admissions.Inquiries;

internal interface IInquiryRepository
{
    Task<Guid> CreateAsync(Guid? cycleId, string prospectiveName, string? guardianName, string guardianPhone,
        string? notes, CancellationToken cancellationToken);
    Task<Inquiry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Inquiry>> ListAsync(string? status, CancellationToken cancellationToken);
    Task SaveAsync(Inquiry inquiry, CancellationToken cancellationToken);
}

internal sealed class InquiryRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? CycleId { get; init; }
    public string ProspectiveName { get; init; } = string.Empty;
    public string? GuardianName { get; init; }
    public string GuardianPhone { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public DateTime? VisitAt { get; init; }
    public string Status { get; init; } = "new";
    public Guid? ConvertedApplicationId { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class InquiryRepository : TenantRepository, IInquiryRepository
{
    private const string Columns =
        "id AS Id, school_id AS OrganizationId, cycle_id AS CycleId, prospective_name AS ProspectiveName, " +
        "guardian_name AS GuardianName, guardian_phone AS GuardianPhone, notes AS Notes, visit_at AS VisitAt, " +
        "status, converted_application_id AS ConvertedApplicationId, created_at AS CreatedAt";

    public InquiryRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
    }

    public Task<Guid> CreateAsync(Guid? cycleId, string prospectiveName, string? guardianName,
        string guardianPhone, string? notes, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO inquiries (school_id, cycle_id, prospective_name, guardian_name, guardian_phone, notes, status)
            VALUES (@SchoolId, @CycleId, @ProspectiveName, @GuardianName, @GuardianPhone, @Notes, 'new')
            RETURNING id
            """,
            TenantParameters(new { CycleId = cycleId, ProspectiveName = prospectiveName,
                GuardianName = guardianName, GuardianPhone = guardianPhone, Notes = notes }),
            cancellationToken);
    }

    public async Task<Inquiry?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        InquiryRow? row = await QuerySingleOrDefaultAsync<InquiryRow>(
            $"SELECT {Columns} FROM inquiries WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = id }), cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public async Task<IReadOnlyList<Inquiry>> ListAsync(string? status, CancellationToken cancellationToken)
    {
        IReadOnlyList<InquiryRow> rows = await QueryAsync<InquiryRow>(
            $"""
            SELECT {Columns} FROM inquiries
            WHERE school_id = @SchoolId AND (@Status IS NULL OR status = @Status)
            ORDER BY created_at DESC
            """,
            TenantParameters(new { Status = status }), cancellationToken);
        return rows.Select(Rehydrate).ToList();
    }

    public Task SaveAsync(Inquiry inquiry, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE inquiries
               SET visit_at = @VisitAt, status = @Status, converted_application_id = @ConvertedApplicationId,
                   updated_at = NOW()
             WHERE id = @Id AND school_id = @SchoolId
            """,
            TenantParameters(new
            {
                Id = inquiry.Id, VisitAt = inquiry.VisitAt, Status = ToDb(inquiry.Status),
                ConvertedApplicationId = inquiry.ConvertedApplicationId
            }),
            cancellationToken);
    }

    private static string ToDb(InquiryStatus status) => status switch
    {
        InquiryStatus.Contacted => "contacted",
        InquiryStatus.VisitBooked => "visit_booked",
        InquiryStatus.Converted => "converted",
        InquiryStatus.Closed => "closed",
        _ => "new"
    };

    private static Inquiry Rehydrate(InquiryRow r) => new(
        r.Id, r.OrganizationId, r.CycleId, r.ProspectiveName, r.GuardianName, r.GuardianPhone, r.Notes,
        r.VisitAt,
        r.Status switch
        {
            "contacted" => InquiryStatus.Contacted,
            "visit_booked" => InquiryStatus.VisitBooked,
            "converted" => InquiryStatus.Converted,
            "closed" => InquiryStatus.Closed,
            _ => InquiryStatus.New
        },
        r.ConvertedApplicationId, r.CreatedAt);
}
