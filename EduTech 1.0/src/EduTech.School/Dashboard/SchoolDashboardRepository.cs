using EduTech.Shared.Persistence;

namespace EduTech.School.Dashboard;

/// <summary>
/// READ MODEL for the school workspace dashboard — aggregate counts/sums projected across
/// contexts (students, attendance, payments, applications, audit). SELECT-only by charter:
/// no writes, no business rules; the owning contexts stay the source of truth (EDD-002).
/// </summary>
internal interface ISchoolDashboardRepository
{
    Task<DashboardStatsRow> GetStatsAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecentApplicationRow>> GetRecentApplicationsAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActivityRow>> GetRecentActivityAsync(Guid schoolId, CancellationToken cancellationToken);
}

internal sealed class DashboardStatsRow
{
    public int StudentsEnrolled { get; init; }
    public int PresentToday { get; init; }
    public int AbsentToday { get; init; }
    public decimal FeesCollectedThisTerm { get; init; }
    public decimal FeeTargetThisTerm { get; init; }
    public int PendingApplications { get; init; }
    public bool ComplianceApproved { get; init; }
}

internal sealed class RecentApplicationRow
{
    public Guid Id { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string? ClassApplied { get; init; }
    public DateTime AppliedAt { get; init; }
    public string Status { get; init; } = string.Empty;
}

internal sealed class ActivityRow
{
    public Guid Id { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

internal sealed class SchoolDashboardRepository : BaseRepository, ISchoolDashboardRepository
{
    public SchoolDashboardRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<DashboardStatsRow> GetStatsAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        return await QuerySingleOrDefaultAsync<DashboardStatsRow>(
            """
            WITH current_term AS (
                SELECT id FROM terms WHERE school_id = @SchoolId AND is_current = TRUE LIMIT 1
            ),
            today AS (
                SELECT
                    COUNT(*) FILTER (WHERE m.status = 'present') AS present,
                    COUNT(*) FILTER (WHERE m.status = 'absent')  AS absent
                FROM attendance_marks m
                JOIN attendance_records r ON r.id = m.attendance_record_id
                WHERE m.school_id = @SchoolId AND r.attendance_date = CURRENT_DATE
            ),
            collected AS (
                SELECT COALESCE(SUM(p.base_amount), 0) AS total
                FROM payments p
                WHERE p.school_id = @SchoolId AND p.status = 'successful'
                  AND p.term_id = (SELECT id FROM current_term)
            ),
            target AS (
                -- Expected this term: every approved COMPULSORY fee × its applicable active students.
                SELECT COALESCE(SUM(ft.amount), 0) AS total
                FROM fee_types ft
                JOIN fee_type_classes ftc ON ftc.fee_type_id = ft.id
                JOIN students s ON s.class_id = ftc.class_id AND s.school_id = @SchoolId AND s.status = 'active'
                WHERE ft.school_id = @SchoolId
                  AND ft.term_id = (SELECT id FROM current_term)
                  AND ft.approval_status = 'approved'
                  AND ft.category = 'compulsory'
            )
            SELECT
                (SELECT COUNT(*) FROM students WHERE school_id = @SchoolId AND status = 'active') AS StudentsEnrolled,
                (SELECT present FROM today) AS PresentToday,
                (SELECT absent FROM today) AS AbsentToday,
                (SELECT total FROM collected) AS FeesCollectedThisTerm,
                (SELECT total FROM target) AS FeeTargetThisTerm,
                (SELECT COUNT(*) FROM applications
                  WHERE school_id = @SchoolId
                    AND status NOT IN ('admitted', 'rejected', 'withdrawn')) AS PendingApplications,
                (SELECT kyc_status = 'approved' FROM schools WHERE id = @SchoolId) AS ComplianceApproved
            """,
            new { SchoolId = schoolId }, cancellationToken) ?? new DashboardStatsRow();
    }

    public Task<IReadOnlyList<RecentApplicationRow>> GetRecentApplicationsAsync(Guid schoolId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<RecentApplicationRow>(
            """
            SELECT a.id AS Id,
                   concat_ws(' ', cp.first_name, cp.last_name) AS StudentName,
                   a.desired_class AS ClassApplied,
                   a.created_at AS AppliedAt,
                   a.status AS Status
            FROM applications a
            JOIN child_profiles cp ON cp.id = a.child_profile_id
            WHERE a.school_id = @SchoolId
            ORDER BY a.created_at DESC
            LIMIT 5
            """,
            new { SchoolId = schoolId }, cancellationToken);
    }

    public Task<IReadOnlyList<ActivityRow>> GetRecentActivityAsync(Guid schoolId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<ActivityRow>(
            """
            SELECT id AS Id, action AS Action, summary AS Summary, created_at AS CreatedAt
            FROM audit_logs
            WHERE school_id = @SchoolId
            ORDER BY created_at DESC
            LIMIT 6
            """,
            new { SchoolId = schoolId }, cancellationToken);
    }
}
