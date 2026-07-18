using EduTech.Admissions.Domain;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Admissions.Cycles;

/// <summary>Per-organization data access for admission cycles (tenant-scoped by the current school).</summary>
internal interface IAdmissionCycleRepository
{
    Task<Guid> CreateAsync(string name, string? intakeType, DateTime? opensAt, DateTime? closesAt,
        int? quota, CancellationToken cancellationToken);
    Task<AdmissionCycle?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdmissionCycle>> ListAsync(string? status, CancellationToken cancellationToken);
    Task SaveAsync(AdmissionCycle cycle, CancellationToken cancellationToken);
}

internal sealed class AdmissionCycleRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? IntakeType { get; init; }
    public DateTime? OpensAt { get; init; }
    public DateTime? ClosesAt { get; init; }
    public int? Quota { get; init; }
    public string Status { get; init; } = "draft";
    public DateTime CreatedAt { get; init; }
}

internal sealed class AdmissionCycleRepository : TenantRepository, IAdmissionCycleRepository
{
    private const string Columns =
        "id AS Id, school_id AS OrganizationId, name AS Name, intake_type AS IntakeType, " +
        "opens_at AS OpensAt, closes_at AS ClosesAt, quota AS Quota, status, created_at AS CreatedAt";

    public AdmissionCycleRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
    }

    public Task<Guid> CreateAsync(string name, string? intakeType, DateTime? opensAt, DateTime? closesAt,
        int? quota, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO admission_cycles (school_id, name, intake_type, opens_at, closes_at, quota, status)
            VALUES (@SchoolId, @Name, @IntakeType, @OpensAt, @ClosesAt, @Quota, 'draft')
            RETURNING id
            """,
            TenantParameters(new { Name = name, IntakeType = intakeType, OpensAt = opensAt,
                ClosesAt = closesAt, Quota = quota }),
            cancellationToken);
    }

    public async Task<AdmissionCycle?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        AdmissionCycleRow? row = await QuerySingleOrDefaultAsync<AdmissionCycleRow>(
            $"SELECT {Columns} FROM admission_cycles WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = id }), cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public async Task<IReadOnlyList<AdmissionCycle>> ListAsync(string? status, CancellationToken cancellationToken)
    {
        IReadOnlyList<AdmissionCycleRow> rows = await QueryAsync<AdmissionCycleRow>(
            $"""
            SELECT {Columns} FROM admission_cycles
            WHERE school_id = @SchoolId AND (@Status IS NULL OR status = @Status)
            ORDER BY created_at DESC
            """,
            TenantParameters(new { Status = status }), cancellationToken);
        return rows.Select(Rehydrate).ToList();
    }

    public Task SaveAsync(AdmissionCycle cycle, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE admission_cycles
               SET name = @Name, intake_type = @IntakeType, opens_at = @OpensAt, closes_at = @ClosesAt,
                   quota = @Quota, status = @Status, updated_at = NOW()
             WHERE id = @Id AND school_id = @SchoolId
            """,
            TenantParameters(new
            {
                Id = cycle.Id, Name = cycle.Name, IntakeType = cycle.IntakeType, OpensAt = cycle.OpensAt,
                ClosesAt = cycle.ClosesAt, Quota = cycle.Quota, Status = ToDb(cycle.Status)
            }),
            cancellationToken);
    }

    private static string ToDb(AdmissionCycleStatus status) => status switch
    {
        AdmissionCycleStatus.Open => "open",
        AdmissionCycleStatus.Closed => "closed",
        AdmissionCycleStatus.Archived => "archived",
        _ => "draft"
    };

    private static AdmissionCycle Rehydrate(AdmissionCycleRow r) => new(
        r.Id, r.OrganizationId, r.Name, r.IntakeType, r.OpensAt, r.ClosesAt, r.Quota,
        r.Status switch
        {
            "open" => AdmissionCycleStatus.Open,
            "closed" => AdmissionCycleStatus.Closed,
            "archived" => AdmissionCycleStatus.Archived,
            _ => AdmissionCycleStatus.Draft
        },
        r.CreatedAt);
}
