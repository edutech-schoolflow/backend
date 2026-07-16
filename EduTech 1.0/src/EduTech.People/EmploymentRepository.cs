using EduTech.People.Domain;
using EduTech.Shared.Persistence;

namespace EduTech.People;

/// <summary>
/// Data access for the canonical <c>employments</c> edge (EDD-009) — where working-relationship
/// lifecycle writes live, so Auth/Workforce drive it rather than owning it.
///
/// During the strangler transition the <c>EnsureFrom…</c> writes read the legacy silos
/// (<c>staff_affiliations</c>, <c>school_owners</c>) to build the canonical employment — a scoped,
/// one-row version of the 0046 backfill. That coupling is temporary and disappears when the silos
/// retire. The table column is still <c>school_id</c>; it re-points to <c>organization_id</c> in Sprint D.
/// </summary>
internal interface IEmploymentRepository
{
    /// <summary>Idempotently records (and activates) the employment behind an active staff affiliation.</summary>
    Task EnsureFromAffiliationAsync(Guid affiliationId, CancellationToken cancellationToken);

    /// <summary>Idempotently records (and activates) the owner's employment.</summary>
    Task EnsureFromOwnerAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Ends the employment(s) behind a staff affiliation (idempotent).</summary>
    Task EndByAffiliationAsync(Guid affiliationId, CancellationToken cancellationToken);

    /// <summary>The active employments at an organization — a canonical read.</summary>
    Task<IReadOnlyList<Domain.Employment>> ListForOrganizationAsync(Guid organizationId,
        CancellationToken cancellationToken);

    /// <summary>An identity's/membership's employments — a canonical read.</summary>
    Task<IReadOnlyList<Domain.Employment>> GetForMembershipAsync(Guid membershipId,
        CancellationToken cancellationToken);
}

internal sealed class EmploymentRow
{
    public Guid Id { get; init; }
    public Guid MembershipId { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? PositionId { get; init; }
    public Guid? OrganizationalUnitId { get; init; }
    public Guid? ManagerEmploymentId { get; init; }
    public string EmploymentType { get; init; } = string.Empty;
    public string Status { get; init; } = "active";
    public DateTime? StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
}

internal sealed class EmploymentRepository : BaseRepository, IEmploymentRepository
{
    private const string SelectColumns =
        "id AS Id, membership_id AS MembershipId, organization_id AS OrganizationId, " +
        "position_id AS PositionId, organizational_unit_id AS OrganizationalUnitId, " +
        "manager_employment_id AS ManagerEmploymentId, employment_type AS EmploymentType, " +
        "status, started_at AS StartedAt, ended_at AS EndedAt";

    public EmploymentRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task EnsureFromAffiliationAsync(Guid affiliationId, CancellationToken cancellationToken)
    {
        // Scoped one-row version of the 0046 staff backfill: link the affiliation to its 'staff'
        // membership and upsert the (membership, position) employment as active.
        return ExecuteAsync(
            """
            INSERT INTO employments (membership_id, organization_id, position_id, employment_type,
                                     status, started_at)
            SELECT m.id, a.school_id, a.position_id, a.employment_type, 'active',
                   COALESCE(a.joined_at, a.created_at, NOW())
            FROM staff_affiliations a
            JOIN memberships m ON m.identity_id = a.identity_id AND m.school_id = a.school_id
                              AND m.kind = 'staff'
            WHERE a.id = @AffiliationId AND a.identity_id IS NOT NULL
            ON CONFLICT (membership_id, position_id)
            DO UPDATE SET status = 'active', ended_at = NULL,
                          employment_type = EXCLUDED.employment_type, updated_at = NOW()
            """,
            new { AffiliationId = affiliationId }, cancellationToken);
    }

    public Task EnsureFromOwnerAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO employments (membership_id, organization_id, position_id, employment_type,
                                     status, started_at)
            SELECT m.id, o.school_id, o.position_id, 'full_time', 'active',
                   COALESCE(o.created_at, NOW())
            FROM school_owners o
            JOIN memberships m ON m.identity_id = o.identity_id AND m.school_id = o.school_id
                              AND m.kind = 'owner'
            WHERE o.id = @OwnerId AND o.identity_id IS NOT NULL
            ON CONFLICT (membership_id, position_id)
            DO UPDATE SET status = 'active', ended_at = NULL, updated_at = NOW()
            """,
            new { OwnerId = ownerId }, cancellationToken);
    }

    public Task EndByAffiliationAsync(Guid affiliationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE employments e SET status = 'ended', ended_at = NOW(), updated_at = NOW()
            FROM staff_affiliations a
            JOIN memberships m ON m.identity_id = a.identity_id AND m.school_id = a.school_id
                              AND m.kind = 'staff'
            WHERE a.id = @AffiliationId AND e.membership_id = m.id AND e.status <> 'ended'
            """,
            new { AffiliationId = affiliationId }, cancellationToken);
    }

    public async Task<IReadOnlyList<Domain.Employment>> ListForOrganizationAsync(Guid organizationId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EmploymentRow> rows = await QueryAsync<EmploymentRow>(
            $"SELECT {SelectColumns} FROM employments WHERE organization_id = @OrganizationId AND status = 'active'",
            new { OrganizationId = organizationId }, cancellationToken);
        return rows.Select(Rehydrate).ToList();
    }

    public async Task<IReadOnlyList<Domain.Employment>> GetForMembershipAsync(Guid membershipId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EmploymentRow> rows = await QueryAsync<EmploymentRow>(
            $"SELECT {SelectColumns} FROM employments WHERE membership_id = @MembershipId",
            new { MembershipId = membershipId }, cancellationToken);
        return rows.Select(Rehydrate).ToList();
    }

    private static Domain.Employment Rehydrate(EmploymentRow r) => new(
        r.Id, r.MembershipId, r.OrganizationId, r.PositionId, r.OrganizationalUnitId,
        r.ManagerEmploymentId, r.EmploymentType,
        r.Status switch
        {
            "draft" => EmploymentStatus.Draft,
            "pending" => EmploymentStatus.Pending,
            "suspended" => EmploymentStatus.Suspended,
            "ended" => EmploymentStatus.Ended,
            _ => EmploymentStatus.Active
        },
        r.StartedAt, r.EndedAt);
}
