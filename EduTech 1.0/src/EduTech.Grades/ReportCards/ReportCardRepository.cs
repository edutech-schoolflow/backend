using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Grades.ReportCards;

internal interface IReportCardRepository
{
    Task<StudentReportInfoRow?> GetStudentAsync(Guid studentId, CancellationToken cancellationToken);
    Task<TermInfoRow?> GetTermAsync(Guid termId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubjectScoreRow>> GetSubjectScoresAsync(Guid studentId, Guid armId, Guid termId, CancellationToken cancellationToken);
    Task<AttendanceSummaryRow> GetAttendanceSummaryAsync(Guid studentId, Guid termId, CancellationToken cancellationToken);
    Task<ReportMetaRow?> GetMetaAsync(Guid studentId, Guid termId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BehavioralRow>> GetBehavioralAsync(Guid reportCardId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportListRow>> ListForArmAsync(Guid armId, Guid termId, CancellationToken cancellationToken);

    /// <summary>Upsert the report's stored fields and REPLACE its behavioral ratings. Status is left untouched.</summary>
    Task UpsertMetaAsync(Guid studentId, Guid termId, Guid? classArmId, string? teacherComment,
        string? principalComment, DateOnly? nextTermResumption,
        IReadOnlyList<(BehavioralTrait Trait, int Score)> behavioral, CancellationToken cancellationToken);

    Task<string?> GetStatusAsync(Guid studentId, Guid termId, CancellationToken cancellationToken);
    /// <summary>Publish one student's report (upsert to published only if draft/new). Null if already published.</summary>
    Task<Guid?> PublishStudentAsync(Guid studentId, Guid termId, Guid? classArmId, CancellationToken cancellationToken);
    /// <summary>Publish every draft/new report for the arm's active students. Returns newly-published student ids.</summary>
    Task<IReadOnlyList<Guid>> PublishArmAsync(Guid armId, Guid termId, CancellationToken cancellationToken);
    /// <summary>Student name + each guardian phone, for publish notifications.</summary>
    Task<IReadOnlyList<NotifyTargetRow>> GetNotifyTargetsAsync(IReadOnlyList<Guid> studentIds, CancellationToken cancellationToken);
}

internal sealed class NotifyTargetRow
{
    public string StudentName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
}

internal sealed class StudentReportInfoRow
{
    public Guid Id { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string? AdmissionNumber { get; init; }
    public Guid? ClassArmId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public string ArmName { get; init; } = string.Empty;
}

internal sealed class TermInfoRow
{
    public string Name { get; init; } = string.Empty;          // snake_case term -> service maps to Term
    public string AcademicYear { get; init; } = string.Empty;
}

internal sealed class SubjectScoreRow
{
    public Guid SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public string AssessmentType { get; init; } = string.Empty;
    public decimal Score { get; init; }
}

internal sealed class AttendanceSummaryRow
{
    public int Present { get; init; }
    public int Absent { get; init; }
    public int Late { get; init; }
    public int Total { get; init; }
}

internal sealed class ReportMetaRow
{
    public Guid Id { get; init; }
    public string? TeacherComment { get; init; }
    public string? PrincipalComment { get; init; }
    public DateOnly? NextTermResumption { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? PublishedAt { get; init; }
}

internal sealed class BehavioralRow
{
    public string Trait { get; init; } = string.Empty;
    public int Score { get; init; }
}

internal sealed class ReportListRow
{
    public Guid StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string? AdmissionNumber { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal? OverallAverage { get; init; }
}

internal sealed class ReportCardRepository : TenantRepository, IReportCardRepository
{
    // Bio lives on the global child_profile (cp) now, joined via students.child_profile_id.
    private const string StudentNameSql = "concat_ws(' ', cp.first_name, cp.middle_name, cp.last_name)";

    private readonly IDbConnectionFactory _connectionFactory;

    public ReportCardRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<StudentReportInfoRow?> GetStudentAsync(Guid studentId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StudentReportInfoRow>(
            $"""
            SELECT s.id, {StudentNameSql} AS StudentName, s.admission_number AS AdmissionNumber,
                   s.class_arm_id AS ClassArmId,
                   COALESCE(c.name, '') AS ClassName,
                   COALESCE(c.name || a.arm, '') AS ArmName
            FROM students s
            JOIN child_profiles cp ON cp.id = s.child_profile_id
            LEFT JOIN class_arms a ON a.id = s.class_arm_id
            LEFT JOIN classes c ON c.id = a.class_id
            WHERE s.id = @Id AND s.school_id = @SchoolId
            """,
            TenantParameters(new { Id = studentId }), cancellationToken);
    }

    public Task<TermInfoRow?> GetTermAsync(Guid termId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<TermInfoRow>(
            """
            SELECT t.name, y.name AS AcademicYear
            FROM terms t
            JOIN academic_years y ON y.id = t.academic_year_id
            WHERE t.id = @Id AND t.school_id = @SchoolId
            """,
            TenantParameters(new { Id = termId }), cancellationToken);
    }

    public Task<IReadOnlyList<SubjectScoreRow>> GetSubjectScoresAsync(Guid studentId, Guid armId, Guid termId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<SubjectScoreRow>(
            """
            SELECT r.subject_id AS SubjectId, sub.name AS SubjectName,
                   r.assessment_type AS AssessmentType, e.score AS Score
            FROM grade_records r
            JOIN subjects sub ON sub.id = r.subject_id
            JOIN grade_entries e ON e.grade_record_id = r.id AND e.student_id = @StudentId
            WHERE r.school_id = @SchoolId AND r.class_arm_id = @ArmId AND r.term_id = @TermId
            ORDER BY sub.name, r.assessment_type
            """,
            TenantParameters(new { StudentId = studentId, ArmId = armId, TermId = termId }), cancellationToken);
    }

    public async Task<AttendanceSummaryRow> GetAttendanceSummaryAsync(Guid studentId, Guid termId,
        CancellationToken cancellationToken)
    {
        return await QuerySingleOrDefaultAsync<AttendanceSummaryRow>(
            """
            SELECT COUNT(*) FILTER (WHERE m.status = 'present')::int AS Present,
                   COUNT(*) FILTER (WHERE m.status = 'absent')::int  AS Absent,
                   COUNT(*) FILTER (WHERE m.status = 'late')::int    AS Late,
                   COUNT(*)::int AS Total
            FROM attendance_marks m
            JOIN attendance_records r ON r.id = m.attendance_record_id
            WHERE m.school_id = @SchoolId AND m.student_id = @StudentId AND r.term_id = @TermId
            """,
            TenantParameters(new { StudentId = studentId, TermId = termId }), cancellationToken)
            ?? new AttendanceSummaryRow();
    }

    public Task<ReportMetaRow?> GetMetaAsync(Guid studentId, Guid termId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ReportMetaRow>(
            """
            SELECT id, teacher_comment AS TeacherComment, principal_comment AS PrincipalComment,
                   next_term_resumption AS NextTermResumption, status, published_at AS PublishedAt
            FROM report_cards
            WHERE student_id = @StudentId AND term_id = @TermId AND school_id = @SchoolId
            """,
            TenantParameters(new { StudentId = studentId, TermId = termId }), cancellationToken);
    }

    public Task<IReadOnlyList<BehavioralRow>> GetBehavioralAsync(Guid reportCardId, CancellationToken cancellationToken)
    {
        return QueryAsync<BehavioralRow>(
            "SELECT trait, score FROM report_behavioral_ratings WHERE report_card_id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = reportCardId }), cancellationToken);
    }

    public Task<IReadOnlyList<ReportListRow>> ListForArmAsync(Guid armId, Guid termId, CancellationToken cancellationToken)
    {
        return QueryAsync<ReportListRow>(
            $"""
            SELECT s.id AS StudentId, {StudentNameSql} AS StudentName, s.admission_number AS AdmissionNumber,
                   COALESCE(rc.status, 'draft') AS Status, avg_t.OverallAverage
            FROM students s
            JOIN child_profiles cp ON cp.id = s.child_profile_id
            LEFT JOIN report_cards rc ON rc.student_id = s.id AND rc.term_id = @TermId AND rc.school_id = @SchoolId
            LEFT JOIN LATERAL (
                SELECT AVG(sub_total.total)::numeric(5,2) AS OverallAverage
                FROM (
                    SELECT SUM(e.score) AS total
                    FROM grade_records r
                    JOIN grade_entries e ON e.grade_record_id = r.id AND e.student_id = s.id
                    WHERE r.school_id = @SchoolId AND r.class_arm_id = @ArmId AND r.term_id = @TermId
                    GROUP BY r.subject_id
                ) sub_total
            ) avg_t ON TRUE
            WHERE s.school_id = @SchoolId AND s.class_arm_id = @ArmId AND s.status = 'active'
            ORDER BY cp.last_name, cp.first_name
            """,
            TenantParameters(new { ArmId = armId, TermId = termId }), cancellationToken);
    }

    public async Task UpsertMetaAsync(Guid studentId, Guid termId, Guid? classArmId, string? teacherComment,
        string? principalComment, DateOnly? nextTermResumption,
        IReadOnlyList<(BehavioralTrait Trait, int Score)> behavioral, CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);

        Guid reportId = await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO report_cards
                (school_id, student_id, term_id, class_arm_id, teacher_comment, principal_comment, next_term_resumption)
            VALUES (@SchoolId, @StudentId, @TermId, @ArmId, @Teacher, @Principal, @Resumption)
            ON CONFLICT (student_id, term_id) DO UPDATE
                SET class_arm_id = EXCLUDED.class_arm_id,
                    teacher_comment = EXCLUDED.teacher_comment,
                    principal_comment = EXCLUDED.principal_comment,
                    next_term_resumption = EXCLUDED.next_term_resumption,
                    updated_at = NOW()
            RETURNING id
            """,
            TenantParameters(new { StudentId = studentId, TermId = termId, ArmId = classArmId,
                Teacher = teacherComment, Principal = principalComment, Resumption = nextTermResumption }),
            cancellationToken, transaction.Transaction);

        await ExecuteAsync(
            "DELETE FROM report_behavioral_ratings WHERE report_card_id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = reportId }), cancellationToken, transaction.Transaction);

        foreach ((BehavioralTrait trait, int score) in behavioral)
        {
            await ExecuteAsync(
                """
                INSERT INTO report_behavioral_ratings (school_id, report_card_id, trait, score)
                VALUES (@SchoolId, @ReportId, @Trait, @Score)
                """,
                TenantParameters(new { ReportId = reportId, Trait = SnakeCaseEnum.ToWire(trait), Score = score }),
                cancellationToken, transaction.Transaction);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task<string?> GetStatusAsync(Guid studentId, Guid termId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string?>(
            "SELECT status FROM report_cards WHERE student_id = @StudentId AND term_id = @TermId AND school_id = @SchoolId",
            TenantParameters(new { StudentId = studentId, TermId = termId }), cancellationToken);
    }

    public Task<Guid?> PublishStudentAsync(Guid studentId, Guid termId, Guid? classArmId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Guid?>(
            """
            INSERT INTO report_cards (school_id, student_id, term_id, class_arm_id, status, published_at)
            VALUES (@SchoolId, @StudentId, @TermId, @ArmId, 'published', NOW())
            ON CONFLICT (student_id, term_id) DO UPDATE
                SET status = 'published', published_at = NOW(), updated_at = NOW()
                WHERE report_cards.status = 'draft'
            RETURNING id
            """,
            TenantParameters(new { StudentId = studentId, TermId = termId, ArmId = classArmId }), cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> PublishArmAsync(Guid armId, Guid termId, CancellationToken cancellationToken)
    {
        return QueryAsync<Guid>(
            """
            INSERT INTO report_cards (school_id, student_id, term_id, class_arm_id, status, published_at)
            SELECT @SchoolId, s.id, @TermId, @ArmId, 'published', NOW()
            FROM students s
            WHERE s.school_id = @SchoolId AND s.class_arm_id = @ArmId AND s.status = 'active'
            ON CONFLICT (student_id, term_id) DO UPDATE
                SET status = 'published', published_at = NOW(), updated_at = NOW()
                WHERE report_cards.status = 'draft'
            RETURNING student_id
            """,
            TenantParameters(new { ArmId = armId, TermId = termId }), cancellationToken);
    }

    public Task<IReadOnlyList<NotifyTargetRow>> GetNotifyTargetsAsync(IReadOnlyList<Guid> studentIds,
        CancellationToken cancellationToken)
    {
        // Notify every guardian: linked parent ACCOUNTS (parent_children) + extra non-account contacts.
        return QueryAsync<NotifyTargetRow>(
            $"""
            SELECT {StudentNameSql} AS StudentName, p.phone AS Phone
            FROM students s
            JOIN child_profiles cp ON cp.id = s.child_profile_id
            JOIN parent_children pc ON pc.child_profile_id = s.child_profile_id
            JOIN parents p ON p.id = pc.parent_id
            WHERE s.school_id = @SchoolId AND s.id = ANY(@Ids)
            UNION ALL
            SELECT {StudentNameSql} AS StudentName, gc.phone AS Phone
            FROM students s
            JOIN child_profiles cp ON cp.id = s.child_profile_id
            JOIN guardian_contacts gc ON gc.child_profile_id = s.child_profile_id
            WHERE s.school_id = @SchoolId AND s.id = ANY(@Ids)
            """,
            TenantParameters(new { Ids = studentIds.ToArray() }), cancellationToken);
    }
}
