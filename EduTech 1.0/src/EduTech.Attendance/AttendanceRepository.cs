using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Attendance;

internal interface IAttendanceRepository
{
    /// <summary>
    /// The register unit (arm or arm-less class) with its responsible class teacher, or null if it isn't
    /// at the current school. Pass a null armId to resolve the whole-class unit.
    /// </summary>
    Task<UnitInfoRow?> GetUnitAsync(Guid classId, Guid? armId, CancellationToken cancellationToken);

    /// <summary>
    /// Units the caller may mark: named arms (they class-teach, or all when owner) plus arm-less classes
    /// (they class-teach, or all when owner).
    /// </summary>
    Task<IReadOnlyList<MarkableArmRow>> ListMarkableArmsAsync(Guid? affiliationId, bool isOwner,
        CancellationToken cancellationToken);

    /// <summary>Active students in the unit, each with their status for the date (null if not marked).</summary>
    Task<IReadOnlyList<RosterStudentRow>> GetRosterAsync(Guid classId, Guid? armId, DateOnly date,
        CancellationToken cancellationToken);

    /// <summary>True if a register already exists for this unit + date.</summary>
    Task<bool> RecordExistsAsync(Guid classId, Guid? armId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Ids of the active students currently in the unit (to validate submitted marks).</summary>
    Task<IReadOnlyList<Guid>> GetActiveStudentIdsAsync(Guid classId, Guid? armId, CancellationToken cancellationToken);

    /// <summary>The school's current term id, if one is set.</summary>
    Task<Guid?> GetCurrentTermIdAsync(CancellationToken cancellationToken);

    /// <summary>The term whose date window contains <paramref name="date"/>, if any.</summary>
    Task<Guid?> GetTermIdForDateAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>True if the school has at least one term with dates set.</summary>
    Task<bool> HasDatedTermsAsync(CancellationToken cancellationToken);

    /// <summary>Upsert the register for unit+date and REPLACE its marks. Returns the record id + submit time.</summary>
    Task<(Guid Id, DateTime SubmittedAt)> UpsertRecordAsync(Guid classId, Guid? armId, DateOnly date, Guid? termId,
        Guid? submittedByAffiliationId, IReadOnlyList<(Guid StudentId, AttendanceStatus Status)> marks,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ArmStatRow>> GetOverviewArmStatsAsync(DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<AbsentStudentRow>> GetAbsentStudentsAsync(DateOnly date, CancellationToken cancellationToken);
}

internal sealed class UnitInfoRow
{
    public Guid ClassId { get; init; }
    public Guid? ArmId { get; init; }
    public string UnitName { get; init; } = string.Empty;
    public Guid? ClassTeacherAffiliationId { get; init; }
}

internal sealed class MarkableArmRow
{
    public Guid? ArmId { get; init; }
    public string ArmName { get; init; } = string.Empty;
    public Guid ClassId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;   // snake_case in DB; service maps to ClassLevel
}

internal sealed class RosterStudentRow
{
    public Guid StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string? AdmissionNumber { get; init; }
    public string? Status { get; init; }   // snake_case in DB (or null); service maps to AttendanceStatus
}

internal sealed class ArmStatRow
{
    public Guid ClassId { get; init; }
    public Guid? ArmId { get; init; }
    public string ArmName { get; init; } = string.Empty;
    public bool Submitted { get; init; }
    public int PresentCount { get; init; }
    public int AbsentCount { get; init; }
    public int LateCount { get; init; }
    public int TotalCount { get; init; }
}

internal sealed class AbsentStudentRow
{
    public string StudentName { get; init; } = string.Empty;
    public string ArmName { get; init; } = string.Empty;
}

internal sealed class AttendanceRepository : TenantRepository, IAttendanceRepository
{
    // Composed student name (first middle? last) and arm name ("JSS 1" + "A" -> "JSS 1A").
    // Bio lives on the global child_profile (cp) now, joined via students.child_profile_id.
    private const string StudentNameSql = "concat_ws(' ', cp.first_name, cp.middle_name, cp.last_name)";
    private const string ArmNameSql = "(c.name || a.arm)";

    private readonly IDbConnectionFactory _connectionFactory;

    public AttendanceRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<UnitInfoRow?> GetUnitAsync(Guid classId, Guid? armId, CancellationToken cancellationToken)
    {
        if (armId is Guid arm)
        {
            return QuerySingleOrDefaultAsync<UnitInfoRow>(
                $"""
                SELECT a.class_id AS ClassId, a.id AS ArmId, {ArmNameSql} AS UnitName,
                       a.class_teacher_affiliation_id AS ClassTeacherAffiliationId
                FROM class_arms a
                JOIN classes c ON c.id = a.class_id
                WHERE a.id = @ArmId AND a.class_id = @ClassId AND a.school_id = @SchoolId
                """,
                TenantParameters(new { ArmId = arm, ClassId = classId }), cancellationToken);
        }

        // The "no-arm" unit: the class's unassigned students, tracked against the class-level teacher.
        // Valid for any class — an arm-less class, or an arms class that still has unassigned students.
        return QuerySingleOrDefaultAsync<UnitInfoRow>(
            """
            SELECT c.id AS ClassId, NULL::uuid AS ArmId,
                   CASE WHEN EXISTS (SELECT 1 FROM class_arms a WHERE a.class_id = c.id)
                        THEN c.name || ' (no arm)' ELSE c.name END AS UnitName,
                   c.class_teacher_affiliation_id AS ClassTeacherAffiliationId
            FROM classes c
            WHERE c.id = @ClassId AND c.school_id = @SchoolId
            """,
            TenantParameters(new { ClassId = classId }), cancellationToken);
    }

    public Task<IReadOnlyList<MarkableArmRow>> ListMarkableArmsAsync(Guid? affiliationId, bool isOwner,
        CancellationToken cancellationToken)
    {
        return QueryAsync<MarkableArmRow>(
            $"""
            SELECT a.id AS ArmId, {ArmNameSql} AS ArmName,
                   c.id AS ClassId, c.name AS ClassName, c.level AS Level,
                   c.display_order AS SortOrder, a.arm AS SortArm
            FROM class_arms a
            JOIN classes c ON c.id = a.class_id
            WHERE a.school_id = @SchoolId
              AND (@IsOwner OR a.class_teacher_affiliation_id = @AffiliationId)
            UNION ALL
            SELECT NULL::uuid AS ArmId,
                   CASE WHEN EXISTS (SELECT 1 FROM class_arms a2 WHERE a2.class_id = c.id)
                        THEN c.name || ' (no arm)' ELSE c.name END AS ArmName,
                   c.id AS ClassId, c.name AS ClassName, c.level AS Level,
                   c.display_order AS SortOrder, '' AS SortArm
            FROM classes c
            WHERE c.school_id = @SchoolId
              AND (@IsOwner OR c.class_teacher_affiliation_id = @AffiliationId)
              AND (
                    NOT EXISTS (SELECT 1 FROM class_arms a2 WHERE a2.class_id = c.id)
                 OR EXISTS (SELECT 1 FROM students s WHERE s.class_id = c.id
                              AND s.class_arm_id IS NULL AND s.status = 'active' AND s.school_id = @SchoolId)
                  )
            ORDER BY SortOrder, ClassName, SortArm
            """,
            TenantParameters(new { IsOwner = isOwner, AffiliationId = affiliationId }), cancellationToken);
    }

    // Roster/record filters: an arm unit matches students in that arm; the "no-arm" unit (armId null)
    // matches the class's students who aren't assigned to any arm.
    private const string UnitStudentFilter =
        "s.class_id = @ClassId AND (s.class_arm_id = @ArmId::uuid OR (@ArmId::uuid IS NULL AND s.class_arm_id IS NULL))";
    private const string UnitRecordFilter =
        "r.class_id = @ClassId AND (r.class_arm_id = @ArmId::uuid OR (@ArmId::uuid IS NULL AND r.class_arm_id IS NULL))";

    public Task<IReadOnlyList<RosterStudentRow>> GetRosterAsync(Guid classId, Guid? armId, DateOnly date,
        CancellationToken cancellationToken)
    {
        return QueryAsync<RosterStudentRow>(
            $"""
            SELECT s.id AS StudentId, {StudentNameSql} AS StudentName,
                   s.admission_number AS AdmissionNumber, m.status AS Status
            FROM students s
            JOIN child_profiles cp ON cp.id = s.child_profile_id
            LEFT JOIN attendance_records r
                   ON {UnitRecordFilter} AND r.attendance_date = @Date AND r.school_id = @SchoolId
            LEFT JOIN attendance_marks m
                   ON m.attendance_record_id = r.id AND m.student_id = s.id
            WHERE s.school_id = @SchoolId AND {UnitStudentFilter} AND s.status = 'active'
            ORDER BY cp.last_name, cp.first_name
            """,
            TenantParameters(new { ClassId = classId, ArmId = armId, Date = date }), cancellationToken);
    }

    public async Task<bool> RecordExistsAsync(Guid classId, Guid? armId, DateOnly date,
        CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM attendance_records r WHERE {UnitRecordFilter} AND r.attendance_date = @Date AND r.school_id = @SchoolId",
            TenantParameters(new { ClassId = classId, ArmId = armId, Date = date }), cancellationToken) > 0;
    }

    public Task<IReadOnlyList<Guid>> GetActiveStudentIdsAsync(Guid classId, Guid? armId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<Guid>(
            $"SELECT s.id FROM students s WHERE s.school_id = @SchoolId AND {UnitStudentFilter} AND s.status = 'active'",
            TenantParameters(new { ClassId = classId, ArmId = armId }), cancellationToken);
    }

    public Task<Guid?> GetCurrentTermIdAsync(CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM terms WHERE school_id = @SchoolId AND is_current = TRUE LIMIT 1",
            TenantParameters(), cancellationToken);
    }

    public Task<Guid?> GetTermIdForDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Guid?>(
            """
            SELECT id FROM terms
            WHERE school_id = @SchoolId AND start_date IS NOT NULL AND end_date IS NOT NULL
              AND @Date BETWEEN start_date AND end_date
            LIMIT 1
            """,
            TenantParameters(new { Date = date }), cancellationToken);
    }

    public async Task<bool> HasDatedTermsAsync(CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM terms
            WHERE school_id = @SchoolId AND start_date IS NOT NULL AND end_date IS NOT NULL
            """,
            TenantParameters(), cancellationToken) > 0;
    }

    public async Task<(Guid Id, DateTime SubmittedAt)> UpsertRecordAsync(Guid classId, Guid? armId, DateOnly date,
        Guid? termId, Guid? submittedByAffiliationId, IReadOnlyList<(Guid StudentId, AttendanceStatus Status)> marks,
        CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        System.Data.IDbTransaction tx = transaction.Transaction;

        // Two partial unique indexes (arm-date, class-date) rule out a single ON CONFLICT target, so
        // upsert manually: find the existing register for this unit+date, then update or insert it.
        Guid? existingId = await QuerySingleOrDefaultAsync<Guid?>(
            $"SELECT id FROM attendance_records r WHERE {UnitRecordFilter} AND r.attendance_date = @Date AND r.school_id = @SchoolId",
            TenantParameters(new { ClassId = classId, ArmId = armId, Date = date }), cancellationToken, tx);

        RecordKeyRow header;
        if (existingId is Guid recordId)
        {
            header = await QuerySingleOrDefaultAsync<RecordKeyRow>(
                """
                UPDATE attendance_records
                   SET term_id = @TermId, submitted_by_affiliation_id = @SubmittedBy,
                       submitted_at = NOW(), updated_at = NOW()
                 WHERE id = @Id AND school_id = @SchoolId
                RETURNING id, submitted_at AS SubmittedAt
                """,
                TenantParameters(new { Id = recordId, TermId = termId, SubmittedBy = submittedByAffiliationId }),
                cancellationToken, tx)
                ?? throw new InvalidOperationException("Attendance record update returned no row.");
        }
        else
        {
            header = await QuerySingleOrDefaultAsync<RecordKeyRow>(
                """
                INSERT INTO attendance_records
                    (school_id, class_id, class_arm_id, term_id, attendance_date, submitted_by_affiliation_id, submitted_at)
                VALUES (@SchoolId, @ClassId, @ArmId, @TermId, @Date, @SubmittedBy, NOW())
                RETURNING id, submitted_at AS SubmittedAt
                """,
                TenantParameters(new { ClassId = classId, ArmId = armId, TermId = termId, Date = date, SubmittedBy = submittedByAffiliationId }),
                cancellationToken, tx)
                ?? throw new InvalidOperationException("Attendance record insert returned no row.");
        }

        // Re-submitting replaces the day's marks.
        await ExecuteAsync(
            "DELETE FROM attendance_marks WHERE attendance_record_id = @RecordId AND school_id = @SchoolId",
            TenantParameters(new { RecordId = header.Id }), cancellationToken, transaction.Transaction);

        foreach ((Guid studentId, AttendanceStatus status) in marks)
        {
            await ExecuteAsync(
                """
                INSERT INTO attendance_marks (school_id, attendance_record_id, student_id, status)
                VALUES (@SchoolId, @RecordId, @StudentId, @Status)
                """,
                TenantParameters(new { RecordId = header.Id, StudentId = studentId, Status = SnakeCaseEnum.ToWire(status) }),
                cancellationToken, transaction.Transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        return (header.Id, header.SubmittedAt);
    }

    public Task<IReadOnlyList<ArmStatRow>> GetOverviewArmStatsAsync(DateOnly date, CancellationToken cancellationToken)
    {
        // One board row per unit: every named arm, plus every arm-less class (as a whole-class unit).
        return QueryAsync<ArmStatRow>(
            $"""
            SELECT c.id AS ClassId, a.id AS ArmId, {ArmNameSql} AS ArmName,
                   (r.id IS NOT NULL) AS Submitted,
                   COUNT(m.id) FILTER (WHERE m.status = 'present')::int AS PresentCount,
                   COUNT(m.id) FILTER (WHERE m.status = 'absent')::int  AS AbsentCount,
                   COUNT(m.id) FILTER (WHERE m.status = 'late')::int    AS LateCount,
                   (SELECT COUNT(*) FROM students s
                      WHERE s.class_arm_id = a.id AND s.status = 'active' AND s.school_id = @SchoolId)::int AS TotalCount,
                   c.display_order AS SortOrder, a.arm AS SortArm
            FROM class_arms a
            JOIN classes c ON c.id = a.class_id
            LEFT JOIN attendance_records r
                   ON r.class_arm_id = a.id AND r.attendance_date = @Date AND r.school_id = @SchoolId
            LEFT JOIN attendance_marks m ON m.attendance_record_id = r.id
            WHERE a.school_id = @SchoolId
            GROUP BY c.id, a.id, c.name, a.arm, c.display_order, r.id
            UNION ALL
            SELECT c.id AS ClassId, NULL::uuid AS ArmId,
                   CASE WHEN EXISTS (SELECT 1 FROM class_arms a2 WHERE a2.class_id = c.id)
                        THEN c.name || ' (no arm)' ELSE c.name END AS ArmName,
                   (r.id IS NOT NULL) AS Submitted,
                   COUNT(m.id) FILTER (WHERE m.status = 'present')::int AS PresentCount,
                   COUNT(m.id) FILTER (WHERE m.status = 'absent')::int  AS AbsentCount,
                   COUNT(m.id) FILTER (WHERE m.status = 'late')::int    AS LateCount,
                   (SELECT COUNT(*) FROM students s
                      WHERE s.class_id = c.id AND s.class_arm_id IS NULL
                        AND s.status = 'active' AND s.school_id = @SchoolId)::int AS TotalCount,
                   c.display_order AS SortOrder, '' AS SortArm
            FROM classes c
            LEFT JOIN attendance_records r
                   ON r.class_id = c.id AND r.class_arm_id IS NULL AND r.attendance_date = @Date AND r.school_id = @SchoolId
            LEFT JOIN attendance_marks m ON m.attendance_record_id = r.id
            WHERE c.school_id = @SchoolId
              AND (
                    NOT EXISTS (SELECT 1 FROM class_arms a2 WHERE a2.class_id = c.id)
                 OR EXISTS (SELECT 1 FROM students s WHERE s.class_id = c.id
                              AND s.class_arm_id IS NULL AND s.status = 'active' AND s.school_id = @SchoolId)
                  )
            GROUP BY c.id, c.name, c.display_order, r.id
            ORDER BY SortOrder, ArmName, SortArm
            """,
            TenantParameters(new { Date = date }), cancellationToken);
    }

    public Task<IReadOnlyList<AbsentStudentRow>> GetAbsentStudentsAsync(DateOnly date, CancellationToken cancellationToken)
    {
        // The unit label is the arm name ("JSS 1A") for arm registers, or the class name for whole-class ones.
        return QueryAsync<AbsentStudentRow>(
            $"""
            SELECT {StudentNameSql} AS StudentName, COALESCE(c.name || a.arm, c.name) AS ArmName
            FROM attendance_marks m
            JOIN attendance_records r ON r.id = m.attendance_record_id
            JOIN classes c ON c.id = r.class_id
            LEFT JOIN class_arms a ON a.id = r.class_arm_id
            JOIN students s ON s.id = m.student_id
            JOIN child_profiles cp ON cp.id = s.child_profile_id
            WHERE r.school_id = @SchoolId AND r.attendance_date = @Date AND m.status = 'absent'
            ORDER BY c.name, a.arm, cp.last_name
            """,
            TenantParameters(new { Date = date }), cancellationToken);
    }

    private sealed class RecordKeyRow
    {
        public Guid Id { get; init; }
        public DateTime SubmittedAt { get; init; }
    }
}
