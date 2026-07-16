using System.Data;
using EduTech.Shared.Persistence;

namespace EduTech.Workforce;

/// <summary>
/// Data access for <c>staff_affiliations</c> — the per-school link for a staff member. Queried by
/// staff_user_id (global identity), not by tenant, so it derives from <see cref="BaseRepository"/>.
/// </summary>
internal interface IStaffAffiliationRepository
{
    /// <summary>True if the staff member already has an ACTIVE full-time affiliation anywhere.</summary>
    Task<bool> HasActiveFullTimeAsync(Guid staffUserId, CancellationToken cancellationToken);

    /// <summary>True if the staff member has ANY active affiliation (used for full-time invites).</summary>
    Task<bool> HasAnyActiveAsync(Guid staffUserId, CancellationToken cancellationToken);

    /// <summary>Existing affiliation for this person at this school (any status), or null.</summary>
    Task<StaffAffiliationRow?> GetAsync(Guid staffUserId, Guid schoolId, CancellationToken cancellationToken);

    Task<StaffAffiliationRow?> GetByIdAsync(Guid affiliationId, CancellationToken cancellationToken);

    /// <summary>Affiliation joined with the school name, for the invite welcome screen.</summary>
    Task<StaffInviteDetailsRow?> GetInviteDetailsAsync(Guid affiliationId, CancellationToken cancellationToken);

    /// <summary>The ACTIVE affiliation for (staff, school) with its template id — for switching/scoping.</summary>
    Task<StaffSwitchRow?> GetActiveForSwitchAsync(Guid staffUserId, Guid schoolId, CancellationToken cancellationToken);

    /// <summary>All of a staff member's active affiliations + school names (the "My Schools" list).</summary>
    Task<IReadOnlyList<StaffSchoolListRow>> ListSchoolsForStaffAsync(Guid staffUserId, CancellationToken cancellationToken);

    /// <summary>Directory: every staff member at a school (any status) with their identity.</summary>
    Task<IReadOnlyList<StaffDirectoryRow>> ListForSchoolAsync(Guid schoolId, CancellationToken cancellationToken);

    /// <summary>A single directory row scoped to the school (null if it isn't this school's staff).</summary>
    Task<StaffDirectoryRow?> GetForSchoolAsync(Guid affiliationId, Guid schoolId, CancellationToken cancellationToken);

    /// <summary>Update role/position for a staff member at this school. Returns rows affected.</summary>
    Task<int> UpdateRoleAsync(Guid affiliationId, Guid schoolId, string role, string? position, CancellationToken cancellationToken);

    /// <summary>Role + permission template per ACTIVE affiliation — feeds effective-feature resolution.</summary>
    Task<IReadOnlyList<AffiliationPermissionMetaRow>> ListPermissionMetaForSchoolAsync(Guid schoolId,
        CancellationToken cancellationToken);

    /// <summary>Set affiliation status (active | inactive) at this school. Returns rows affected.</summary>
    Task<int> SetStatusAsync(Guid affiliationId, Guid schoolId, string status, CancellationToken cancellationToken);

    /// <summary>The identity linked to this affiliation at the school (null if unlinked or not found).</summary>
    Task<Guid?> GetIdentityIdAsync(Guid affiliationId, Guid schoolId, CancellationToken cancellationToken);

    Task<Guid> CreateInvitedAsync(Guid staffUserId, Guid schoolId, string role, string? position,
        string employmentType, Guid? invitedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Re-invites a previously resigned/inactive affiliation (the UNIQUE(staff_user_id, school_id)
    /// constraint blocks a fresh insert), resetting it to 'invited' with new terms.
    /// </summary>
    Task ReInviteAsync(Guid affiliationId, string role, string? position, string employmentType,
        Guid? invitedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    Task ActivateAsync(Guid affiliationId, IDbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>An affiliation row.</summary>
internal sealed class StaffAffiliationRow
{
    public Guid Id { get; init; }
    public Guid StaffUserId { get; init; }
    public Guid SchoolId { get; init; }
    public string Role { get; init; } = string.Empty;
    public string? Position { get; init; }
    public string EmploymentType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

/// <summary>A staff member as seen in the school's directory (affiliation + identity).</summary>
internal sealed class StaffDirectoryRow
{
    public Guid Id { get; init; }                 // affiliation id (the per-school record)
    public Guid StaffUserId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string Role { get; init; } = string.Empty;
    public string? Position { get; init; }
    public string EmploymentType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;   // invited | active | inactive
    public DateTime? JoinedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Affiliation + school name, for the invite welcome screen.</summary>
internal sealed class StaffInviteDetailsRow
{
    public Guid StaffUserId { get; init; }
    public Guid SchoolId { get; init; }
    public string Role { get; init; } = string.Empty;
    public string EmploymentType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? SchoolName { get; init; }   // null until set during KYC
}

/// <summary>Active affiliation fields needed to mint a school-scoped token.</summary>
internal sealed class AffiliationPermissionMetaRow
{
    public Guid AffiliationId { get; init; }
    public string Role { get; init; } = string.Empty;
    public Guid? PermissionTemplateId { get; init; }
}

internal sealed class StaffSwitchRow
{
    public Guid AffiliationId { get; init; }
    public string Role { get; init; } = string.Empty;
    public string EmploymentType { get; init; } = string.Empty;
    public Guid? PermissionTemplateId { get; init; }
}

/// <summary>One entry in the staff member's "My Schools" list.</summary>
internal sealed class StaffSchoolListRow
{
    public Guid SchoolId { get; init; }
    public string? SchoolName { get; init; }
    public string Role { get; init; } = string.Empty;
    public string? Position { get; init; }
    public string EmploymentType { get; init; } = string.Empty;
}

internal sealed class StaffAffiliationRepository : BaseRepository, IStaffAffiliationRepository
{
    public StaffAffiliationRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<bool> HasActiveFullTimeAsync(Guid staffUserId, CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM staff_affiliations
            WHERE staff_user_id = @Id AND employment_type = 'full_time' AND status = 'active'
            """,
            new { Id = staffUserId }, cancellationToken);
        return count > 0;
    }

    public async Task<bool> HasAnyActiveAsync(Guid staffUserId, CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM staff_affiliations WHERE staff_user_id = @Id AND status = 'active'",
            new { Id = staffUserId }, cancellationToken);
        return count > 0;
    }

    public Task<StaffAffiliationRow?> GetAsync(Guid staffUserId, Guid schoolId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StaffAffiliationRow>(
            """
            SELECT id, staff_user_id, school_id, role, position, employment_type, status
            FROM staff_affiliations
            WHERE staff_user_id = @StaffUserId AND school_id = @SchoolId
            """,
            new { StaffUserId = staffUserId, SchoolId = schoolId }, cancellationToken);
    }

    public Task<StaffAffiliationRow?> GetByIdAsync(Guid affiliationId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StaffAffiliationRow>(
            """
            SELECT id, staff_user_id, school_id, role, position, employment_type, status
            FROM staff_affiliations
            WHERE id = @Id
            """,
            new { Id = affiliationId }, cancellationToken);
    }

    public Task<StaffInviteDetailsRow?> GetInviteDetailsAsync(Guid affiliationId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StaffInviteDetailsRow>(
            """
            SELECT a.staff_user_id, a.school_id, a.role, a.employment_type, a.status, s.name AS school_name
            FROM staff_affiliations a
            JOIN schools s ON s.id = a.school_id
            WHERE a.id = @Id
            """,
            new { Id = affiliationId }, cancellationToken);
    }

    public Task<StaffSwitchRow?> GetActiveForSwitchAsync(Guid staffUserId, Guid schoolId,
        CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StaffSwitchRow>(
            """
            SELECT id AS affiliation_id, role, employment_type, permission_template_id
            FROM staff_affiliations
            WHERE staff_user_id = @StaffUserId AND school_id = @SchoolId AND status = 'active'
            """,
            new { StaffUserId = staffUserId, SchoolId = schoolId }, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffSchoolListRow>> ListSchoolsForStaffAsync(Guid staffUserId,
        CancellationToken cancellationToken)
    {
        return await QueryAsync<StaffSchoolListRow>(
            """
            SELECT a.school_id, s.name AS school_name, a.role, a.position, a.employment_type
            FROM staff_affiliations a
            JOIN schools s ON s.id = a.school_id
            WHERE a.staff_user_id = @StaffUserId AND a.status = 'active'
            ORDER BY s.name
            """,
            new { StaffUserId = staffUserId }, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffDirectoryRow>> ListForSchoolAsync(Guid schoolId,
        CancellationToken cancellationToken)
    {
        return await QueryAsync<StaffDirectoryRow>(
            """
            SELECT a.id, a.staff_user_id, u.first_name, u.middle_name, u.last_name, u.phone, u.email,
                   a.role, a.position, a.employment_type, a.status, a.joined_at, a.created_at
            FROM staff_affiliations a
            JOIN staff_users u ON u.id = a.staff_user_id
            WHERE a.school_id = @SchoolId
            ORDER BY a.created_at DESC
            """,
            new { SchoolId = schoolId }, cancellationToken);
    }

    public Task<IReadOnlyList<AffiliationPermissionMetaRow>> ListPermissionMetaForSchoolAsync(Guid schoolId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<AffiliationPermissionMetaRow>(
            """
            SELECT id AS AffiliationId, role AS Role, permission_template_id AS PermissionTemplateId
            FROM staff_affiliations
            WHERE school_id = @SchoolId AND status = 'active'
            """,
            new { SchoolId = schoolId }, cancellationToken);
    }

    public Task<StaffDirectoryRow?> GetForSchoolAsync(Guid affiliationId, Guid schoolId,
        CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StaffDirectoryRow>(
            """
            SELECT a.id, a.staff_user_id, u.first_name, u.middle_name, u.last_name, u.phone, u.email,
                   a.role, a.position, a.employment_type, a.status, a.joined_at, a.created_at
            FROM staff_affiliations a
            JOIN staff_users u ON u.id = a.staff_user_id
            WHERE a.id = @Id AND a.school_id = @SchoolId
            """,
            new { Id = affiliationId, SchoolId = schoolId }, cancellationToken);
    }

    public Task<int> UpdateRoleAsync(Guid affiliationId, Guid schoolId, string role, string? position,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE staff_affiliations SET role = @Role, position = @Position, updated_at = NOW()
            WHERE id = @Id AND school_id = @SchoolId
            """,
            new { Id = affiliationId, SchoolId = schoolId, Role = role, Position = position }, cancellationToken);
    }

    public Task<int> SetStatusAsync(Guid affiliationId, Guid schoolId, string status,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE staff_affiliations SET status = @Status, updated_at = NOW()
            WHERE id = @Id AND school_id = @SchoolId
            """,
            new { Id = affiliationId, SchoolId = schoolId, Status = status }, cancellationToken);
    }

    public Task<Guid?> GetIdentityIdAsync(Guid affiliationId, Guid schoolId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Guid?>(
            "SELECT identity_id FROM staff_affiliations WHERE id = @Id AND school_id = @SchoolId",
            new { Id = affiliationId, SchoolId = schoolId }, cancellationToken);
    }

    public async Task<Guid> CreateInvitedAsync(Guid staffUserId, Guid schoolId, string role, string? position,
        string employmentType, Guid? invitedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO staff_affiliations
                (staff_user_id, school_id, role, position, employment_type, invited_by, status)
            VALUES (@StaffUserId, @SchoolId, @Role, @Position, @EmploymentType, @InvitedBy, 'invited')
            RETURNING id
            """,
            new
            {
                StaffUserId = staffUserId,
                SchoolId = schoolId,
                Role = role,
                Position = position,
                EmploymentType = employmentType,
                InvitedBy = invitedBy
            },
            cancellationToken, transaction);
    }

    public Task ReInviteAsync(Guid affiliationId, string role, string? position, string employmentType,
        Guid? invitedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE staff_affiliations
            SET status = 'invited', role = @Role, position = @Position,
                employment_type = @EmploymentType, invited_by = @InvitedBy,
                joined_at = NULL, updated_at = NOW()
            WHERE id = @Id
            """,
            new
            {
                Id = affiliationId,
                Role = role,
                Position = position,
                EmploymentType = employmentType,
                InvitedBy = invitedBy
            },
            cancellationToken, transaction);
    }

    public Task ActivateAsync(Guid affiliationId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE staff_affiliations
            SET status = 'active', joined_at = NOW(), updated_at = NOW()
            WHERE id = @Id
            """,
            new { Id = affiliationId }, cancellationToken, transaction);
    }
}
