using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Grades.Scores;

internal interface IGradeRepository
{
    Task<ArmGradingRow?> GetArmAsync(Guid armId, CancellationToken cancellationToken);
    Task<SubjectInfoRow?> GetSubjectAsync(Guid subjectId, CancellationToken cancellationToken);
    Task<bool> TermExistsAsync(Guid termId, CancellationToken cancellationToken);
    Task<bool> IsSubjectTeacherAsync(Guid armId, string subjectName, Guid affiliationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GradeableArmRow>> ListGradeableArmsAsync(Guid? affiliationId, bool isOwner, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetActiveStudentIdsAsync(Guid armId, CancellationToken cancellationToken);

    Task<GradeRecordHeaderRow?> GetRecordHeaderAsync(Guid armId, Guid subjectId, Guid termId,
        AssessmentType assessmentType, CancellationToken cancellationToken);
    Task<IReadOnlyList<GradeRosterRow>> GetRosterAsync(Guid armId, Guid subjectId, Guid termId,
        AssessmentType assessmentType, CancellationToken cancellationToken);

    /// <summary>
    /// Upsert the record and REPLACE its entries — guarded so it only touches a DRAFT record. Returns
    /// id + submit time, or null when the record is already published (nothing was written).
    /// </summary>
    Task<(Guid Id, DateTime SubmittedAt)?> UpsertRecordAsync(Guid armId, Guid subjectId, Guid termId,
        AssessmentType assessmentType, int maxScore, Guid? submittedByAffiliationId,
        IReadOnlyList<(Guid StudentId, decimal Score)> entries, CancellationToken cancellationToken);

    /// <summary>The record's identity for authorization (arm + subject) and its status; null if not found.</summary>
    Task<GradeRecordKeyRow?> GetRecordKeyAsync(Guid recordId, CancellationToken cancellationToken);
    /// <summary>Publish only if still draft (race-safe). Returns rows affected.</summary>
    Task<int> PublishRecordIfDraftAsync(Guid recordId, CancellationToken cancellationToken);
    /// <summary>Publish every draft record for a term (optionally one arm). Returns count published.</summary>
    Task<int> PublishAllDraftAsync(Guid termId, Guid? armId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GradeOverviewRow>> GetOverviewAsync(Guid termId, CancellationToken cancellationToken);
}

internal sealed class GradeOverviewRow
{
    public Guid RecordId { get; init; }
    public Guid ArmId { get; init; }
    public string ArmName { get; init; } = string.Empty;
    public Guid SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public string AssessmentType { get; init; } = string.Empty;
    public int MaxScore { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal AverageScore { get; init; }
    public int PassCount { get; init; }
    public int FailCount { get; init; }
    public int TotalCount { get; init; }
    public DateTime SubmittedAt { get; init; }
}

internal sealed class ArmGradingRow
{
    public Guid Id { get; init; }
    public string ArmName { get; init; } = string.Empty;
    public Guid ClassId { get; init; }
    public string Level { get; init; } = string.Empty;
    public Guid? ClassTeacherAffiliationId { get; init; }
}

internal sealed class SubjectInfoRow
{
    public Guid Id { get; init; }
    public Guid ClassId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int MaxCa { get; init; }
    public int MaxExam { get; init; }
}

internal sealed class GradeableArmRow
{
    public Guid ArmId { get; init; }
    public string ArmName { get; init; } = string.Empty;
    public Guid ClassId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;
}

internal sealed class GradeRosterRow
{
    public Guid StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string? AdmissionNumber { get; init; }
    public decimal? Score { get; init; }
}

internal sealed class GradeRecordHeaderRow
{
    public Guid Id { get; init; }
    public int MaxScore { get; init; }
    public string Status { get; init; } = string.Empty;
}

internal sealed class GradeRecordKeyRow
{
    public Guid Id { get; init; }
    public Guid ArmId { get; init; }
    public Guid SubjectId { get; init; }
    public string Status { get; init; } = string.Empty;
}

internal sealed class GradeRepository : TenantRepository, IGradeRepository
{
    // Bio lives on the global child_profile (cp) now, joined via students.child_profile_id.
    private const string StudentNameSql = "concat_ws(' ', cp.first_name, cp.middle_name, cp.last_name)";
    private const string ArmNameSql = "(c.name || a.arm)";

    private readonly IDbConnectionFactory _connectionFactory;

    public GradeRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<ArmGradingRow?> GetArmAsync(Guid armId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ArmGradingRow>(
            $"""
            SELECT a.id, {ArmNameSql} AS ArmName, a.class_id AS ClassId, c.level AS Level,
                   a.class_teacher_affiliation_id AS ClassTeacherAffiliationId
            FROM class_arms a
            JOIN classes c ON c.id = a.class_id
            WHERE a.id = @Id AND a.school_id = @SchoolId
            """,
            TenantParameters(new { Id = armId }), cancellationToken);
    }

    public Task<SubjectInfoRow?> GetSubjectAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<SubjectInfoRow>(
            """
            SELECT id, class_id AS ClassId, name, max_ca AS MaxCa, max_exam AS MaxExam
            FROM subjects WHERE id = @Id AND school_id = @SchoolId
            """,
            TenantParameters(new { Id = subjectId }), cancellationToken);
    }

    public async Task<bool> TermExistsAsync(Guid termId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM terms WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = termId }), cancellationToken) > 0;
    }

    public async Task<bool> IsSubjectTeacherAsync(Guid armId, string subjectName, Guid affiliationId,
        CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM class_subject_teachers
            WHERE school_id = @SchoolId AND class_arm_id = @ArmId
              AND teacher_affiliation_id = @Aff AND lower(subject) = lower(@Subject)
            """,
            TenantParameters(new { ArmId = armId, Aff = affiliationId, Subject = subjectName }), cancellationToken) > 0;
    }

    public Task<IReadOnlyList<GradeableArmRow>> ListGradeableArmsAsync(Guid? affiliationId, bool isOwner,
        CancellationToken cancellationToken)
    {
        return QueryAsync<GradeableArmRow>(
            $"""
            SELECT a.id AS ArmId, {ArmNameSql} AS ArmName, c.id AS ClassId, c.name AS ClassName, c.level AS Level
            FROM class_arms a
            JOIN classes c ON c.id = a.class_id
            WHERE a.school_id = @SchoolId AND (
                  @IsOwner
               OR (c.level IN ('pre_school','nursery','primary') AND a.class_teacher_affiliation_id = @Aff)
               OR (c.level IN ('junior_secondary','senior_secondary') AND EXISTS (
                     SELECT 1 FROM class_subject_teachers st
                     WHERE st.class_arm_id = a.id AND st.teacher_affiliation_id = @Aff))
            )
            ORDER BY c.display_order, c.name, a.arm
            """,
            TenantParameters(new { IsOwner = isOwner, Aff = affiliationId }), cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> GetActiveStudentIdsAsync(Guid armId, CancellationToken cancellationToken)
    {
        return QueryAsync<Guid>(
            "SELECT id FROM students WHERE school_id = @SchoolId AND class_arm_id = @ArmId AND status = 'active'",
            TenantParameters(new { ArmId = armId }), cancellationToken);
    }

    public Task<GradeRecordHeaderRow?> GetRecordHeaderAsync(Guid armId, Guid subjectId, Guid termId,
        AssessmentType assessmentType, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<GradeRecordHeaderRow>(
            """
            SELECT id, max_score AS MaxScore, status
            FROM grade_records
            WHERE school_id = @SchoolId AND class_arm_id = @ArmId AND subject_id = @SubjectId
              AND term_id = @TermId AND assessment_type = @Assessment
            """,
            TenantParameters(new { ArmId = armId, SubjectId = subjectId, TermId = termId,
                Assessment = SnakeCaseEnum.ToWire(assessmentType) }), cancellationToken);
    }

    public Task<IReadOnlyList<GradeRosterRow>> GetRosterAsync(Guid armId, Guid subjectId, Guid termId,
        AssessmentType assessmentType, CancellationToken cancellationToken)
    {
        return QueryAsync<GradeRosterRow>(
            $"""
            SELECT s.id AS StudentId, {StudentNameSql} AS StudentName, s.admission_number AS AdmissionNumber,
                   e.score AS Score
            FROM students s
            JOIN child_profiles cp ON cp.id = s.child_profile_id
            LEFT JOIN grade_records r
                   ON r.class_arm_id = @ArmId AND r.subject_id = @SubjectId AND r.term_id = @TermId
                  AND r.assessment_type = @Assessment AND r.school_id = @SchoolId
            LEFT JOIN grade_entries e ON e.grade_record_id = r.id AND e.student_id = s.id
            WHERE s.school_id = @SchoolId AND s.class_arm_id = @ArmId AND s.status = 'active'
            ORDER BY cp.last_name, cp.first_name
            """,
            TenantParameters(new { ArmId = armId, SubjectId = subjectId, TermId = termId,
                Assessment = SnakeCaseEnum.ToWire(assessmentType) }), cancellationToken);
    }

    public async Task<(Guid Id, DateTime SubmittedAt)?> UpsertRecordAsync(Guid armId, Guid subjectId, Guid termId,
        AssessmentType assessmentType, int maxScore, Guid? submittedByAffiliationId,
        IReadOnlyList<(Guid StudentId, decimal Score)> entries, CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);

        // The DO UPDATE is fenced on status so a submit can never rewrite a record that a concurrent
        // publish just released; no row back ⇒ published ⇒ nothing (incl. entries) is written.
        HeaderKeyRow? header = await QuerySingleOrDefaultAsync<HeaderKeyRow>(
            """
            INSERT INTO grade_records
                (school_id, class_arm_id, subject_id, term_id, assessment_type, max_score,
                 submitted_by_affiliation_id, submitted_at)
            VALUES (@SchoolId, @ArmId, @SubjectId, @TermId, @Assessment, @MaxScore, @SubmittedBy, NOW())
            ON CONFLICT (class_arm_id, subject_id, term_id, assessment_type) DO UPDATE
                SET max_score = EXCLUDED.max_score,
                    submitted_by_affiliation_id = EXCLUDED.submitted_by_affiliation_id,
                    submitted_at = NOW(),
                    updated_at = NOW()
                WHERE grade_records.status = 'draft'
            RETURNING id, submitted_at AS SubmittedAt
            """,
            TenantParameters(new { ArmId = armId, SubjectId = subjectId, TermId = termId,
                Assessment = SnakeCaseEnum.ToWire(assessmentType), MaxScore = maxScore, SubmittedBy = submittedByAffiliationId }),
            cancellationToken, transaction.Transaction);

        if (header is null)
        {
            return null;
        }

        await ExecuteAsync(
            "DELETE FROM grade_entries WHERE grade_record_id = @RecordId AND school_id = @SchoolId",
            TenantParameters(new { RecordId = header.Id }), cancellationToken, transaction.Transaction);

        foreach ((Guid studentId, decimal score) in entries)
        {
            await ExecuteAsync(
                """
                INSERT INTO grade_entries (school_id, grade_record_id, student_id, score)
                VALUES (@SchoolId, @RecordId, @StudentId, @Score)
                """,
                TenantParameters(new { RecordId = header.Id, StudentId = studentId, Score = score }),
                cancellationToken, transaction.Transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        return (header.Id, header.SubmittedAt);
    }

    public Task<GradeRecordKeyRow?> GetRecordKeyAsync(Guid recordId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<GradeRecordKeyRow>(
            """
            SELECT id, class_arm_id AS ArmId, subject_id AS SubjectId, status
            FROM grade_records WHERE id = @Id AND school_id = @SchoolId
            """,
            TenantParameters(new { Id = recordId }), cancellationToken);
    }

    public Task<int> PublishRecordIfDraftAsync(Guid recordId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE grade_records SET status = 'published', published_at = NOW(), updated_at = NOW()
            WHERE id = @Id AND school_id = @SchoolId AND status = 'draft'
            """,
            TenantParameters(new { Id = recordId }), cancellationToken);
    }

    public Task<int> PublishAllDraftAsync(Guid termId, Guid? armId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE grade_records SET status = 'published', published_at = NOW(), updated_at = NOW()
            WHERE school_id = @SchoolId AND term_id = @TermId AND status = 'draft'
              AND (@ArmId IS NULL OR class_arm_id = @ArmId)
            """,
            TenantParameters(new { TermId = termId, ArmId = armId }), cancellationToken);
    }

    public Task<IReadOnlyList<GradeOverviewRow>> GetOverviewAsync(Guid termId, CancellationToken cancellationToken)
    {
        return QueryAsync<GradeOverviewRow>(
            $"""
            SELECT r.id AS RecordId, r.class_arm_id AS ArmId, {ArmNameSql} AS ArmName,
                   r.subject_id AS SubjectId, sub.name AS SubjectName,
                   r.assessment_type AS AssessmentType, r.max_score AS MaxScore, r.status,
                   COALESCE(AVG(e.score), 0)::numeric(5,2) AS AverageScore,
                   COUNT(e.id) FILTER (WHERE e.score >= r.max_score * 0.4)::int AS PassCount,
                   COUNT(e.id) FILTER (WHERE e.score <  r.max_score * 0.4)::int AS FailCount,
                   COUNT(e.id)::int AS TotalCount,
                   r.submitted_at AS SubmittedAt
            FROM grade_records r
            JOIN class_arms a ON a.id = r.class_arm_id
            JOIN classes c ON c.id = a.class_id
            JOIN subjects sub ON sub.id = r.subject_id
            LEFT JOIN grade_entries e ON e.grade_record_id = r.id
            WHERE r.school_id = @SchoolId AND r.term_id = @TermId
            GROUP BY r.id, c.name, a.arm, sub.name, r.assessment_type, r.max_score, r.status, r.submitted_at
            ORDER BY c.name, a.arm, sub.name
            """,
            TenantParameters(new { TermId = termId }), cancellationToken);
    }

    private sealed class HeaderKeyRow
    {
        public Guid Id { get; init; }
        public DateTime SubmittedAt { get; init; }
    }
}
