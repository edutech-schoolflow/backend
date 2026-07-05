using EduTech.Shared.Persistence;

namespace EduTech.Students.ParentFacing;

/// <summary>
/// Parent-facing reads. Parents are school-agnostic (no school_id in their JWT), so this uses
/// <see cref="BaseRepository"/> and authorizes by OWNERSHIP — every query is scoped to the parent via
/// <c>parent_children</c> (parent → child_profile → active student → that school's data).
/// </summary>
internal interface IParentChildrenRepository
{
    Task<bool> OwnsChildAsync(Guid parentId, Guid childProfileId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ParentChildRow>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken);
    Task<ChildProfileDetailRow?> GetChildProfileAsync(Guid childProfileId, CancellationToken cancellationToken);
    Task<Guid> InsertChildProfileAsync(Guid parentId, ChildProfileInsert insert, string? relationship, CancellationToken cancellationToken);
    Task UpdateChildProfileAsync(Guid childProfileId, ChildProfileInsert insert, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildReportCardRow>> GetReportCardsAsync(Guid childProfileId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildCaScoreRow>> GetCaScoresAsync(Guid childProfileId, Guid? termId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildAttendanceRow>> GetAttendanceAsync(Guid childProfileId, CancellationToken cancellationToken);
}

internal sealed class ChildProfileInsert
{
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public required DateOnly DateOfBirth { get; init; }
    public string? Gender { get; init; }            // snake_case wire string or null
    public string? PhotoUrl { get; init; }
    public string? PreviousSchool { get; init; }
    public string? MedicalInfo { get; init; }
}

internal sealed class ParentChildRow
{
    public Guid ChildProfileId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public Guid? StudentId { get; init; }
    public Guid? SchoolId { get; init; }
    public string? SchoolName { get; init; }
    public string? SchoolLogoUrl { get; init; }
    public string? ClassName { get; init; }
    public string? AdmissionNumber { get; init; }
    public string? EnrollmentStatus { get; init; }
    public decimal OutstandingFees { get; init; }
    public bool HasNewResult { get; init; }
}

internal sealed class ChildProfileDetailRow
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public DateOnly DateOfBirth { get; init; }
    public string? Gender { get; init; }   // snake_case wire string
    public string? PhotoUrl { get; init; }
    public string? PreviousSchool { get; init; }
    public string? MedicalInfo { get; init; }
}

internal sealed class ChildReportCardRow
{
    public Guid Id { get; init; }
    public string? Term { get; init; }
    public string? AcademicYear { get; init; }
    public string? SchoolName { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? PublishedAt { get; init; }
}

internal sealed class ChildCaScoreRow
{
    public string SubjectName { get; init; } = string.Empty;
    public string AssessmentType { get; init; } = string.Empty;
    public decimal? Score { get; init; }
    public int MaxScore { get; init; }
    public Guid TermId { get; init; }
}

internal sealed class ChildAttendanceRow
{
    public string? Term { get; init; }
    public int PresentDays { get; init; }
    public int AbsentDays { get; init; }
    public int LateDays { get; init; }
    public int TotalDays { get; init; }
}

internal sealed class ParentChildrenRepository : BaseRepository, IParentChildrenRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ParentChildrenRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> OwnsChildAsync(Guid parentId, Guid childProfileId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM parent_children WHERE parent_id = @ParentId AND child_profile_id = @ChildProfileId",
            new { ParentId = parentId, ChildProfileId = childProfileId }, cancellationToken) > 0;
    }

    public Task<IReadOnlyList<ParentChildRow>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken)
    {
        return QueryAsync<ParentChildRow>(
            """
            SELECT cp.id AS ChildProfileId,
                   concat_ws(' ', cp.first_name, cp.middle_name, cp.last_name) AS StudentName,
                   st.id AS StudentId, st.school_id AS SchoolId, sch.name AS SchoolName, sch.logo_url AS SchoolLogoUrl,
                   NULLIF(concat_ws('', cl.name, ca.arm), '') AS ClassName,
                   st.admission_number AS AdmissionNumber, st.status AS EnrollmentStatus,
                   COALESCE((
                       SELECT SUM(GREATEST(ft.amount - COALESCE((
                                  SELECT SUM(p.base_amount) FROM payments p
                                  WHERE p.student_id = st.id AND p.fee_type_id = ft.id AND p.status = 'successful'), 0), 0))
                       FROM terms t
                       JOIN fee_types ft ON ft.school_id = st.school_id AND ft.term_id = t.id
                                        AND ft.approval_status = 'approved' AND ft.is_active = TRUE
                       JOIN fee_type_classes ftc ON ftc.fee_type_id = ft.id AND ftc.class_id = st.class_id
                       WHERE t.school_id = st.school_id AND t.is_current = TRUE
                         AND (ft.category = 'compulsory'
                              OR EXISTS (SELECT 1 FROM fee_subscriptions fs
                                         WHERE fs.student_id = st.id AND fs.fee_type_id = ft.id))
                   ), 0) AS OutstandingFees,
                   COALESCE((SELECT TRUE FROM report_cards rc
                             WHERE rc.student_id = st.id AND rc.status = 'published' LIMIT 1), FALSE) AS HasNewResult
            FROM parent_children pc
            JOIN child_profiles cp ON cp.id = pc.child_profile_id
            LEFT JOIN students st ON st.child_profile_id = cp.id AND st.status = 'active'
            LEFT JOIN schools sch ON sch.id = st.school_id
            LEFT JOIN classes cl ON cl.id = st.class_id
            LEFT JOIN class_arms ca ON ca.id = st.class_arm_id
            WHERE pc.parent_id = @ParentId
            ORDER BY cp.first_name, cp.last_name
            """,
            new { ParentId = parentId }, cancellationToken);
    }

    public Task<ChildProfileDetailRow?> GetChildProfileAsync(Guid childProfileId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ChildProfileDetailRow?>(
            """
            SELECT id AS Id, first_name AS FirstName, middle_name AS MiddleName, last_name AS LastName,
                   date_of_birth AS DateOfBirth, gender AS Gender, photo_url AS PhotoUrl,
                   previous_school AS PreviousSchool, medical_info AS MedicalInfo
            FROM child_profiles
            WHERE id = @Id
            """,
            new { Id = childProfileId }, cancellationToken);
    }

    public async Task<Guid> InsertChildProfileAsync(Guid parentId, ChildProfileInsert insert, string? relationship,
        CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);

        Guid childProfileId = await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO child_profiles
                (parent_id, first_name, middle_name, last_name, date_of_birth, gender, photo_url,
                 previous_school, medical_info)
            VALUES (@ParentId, @FirstName, @MiddleName, @LastName, @DateOfBirth, @Gender, @PhotoUrl,
                 @PreviousSchool, @MedicalInfo)
            RETURNING id
            """,
            new
            {
                ParentId = parentId, insert.FirstName, insert.MiddleName, insert.LastName, insert.DateOfBirth,
                insert.Gender, insert.PhotoUrl, insert.PreviousSchool, insert.MedicalInfo
            },
            cancellationToken, transaction.Transaction);

        await ExecuteAsync(
            """
            INSERT INTO parent_children (parent_id, child_profile_id, relationship, is_primary)
            VALUES (@ParentId, @ChildProfileId, @Relationship, TRUE)
            ON CONFLICT (parent_id, child_profile_id) DO NOTHING
            """,
            new { ParentId = parentId, ChildProfileId = childProfileId, Relationship = relationship },
            cancellationToken, transaction.Transaction);

        await transaction.CommitAsync(cancellationToken);
        return childProfileId;
    }

    public Task UpdateChildProfileAsync(Guid childProfileId, ChildProfileInsert insert, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE child_profiles
               SET first_name = @FirstName, middle_name = @MiddleName, last_name = @LastName,
                   date_of_birth = @DateOfBirth, gender = @Gender, photo_url = @PhotoUrl,
                   previous_school = @PreviousSchool, medical_info = @MedicalInfo, updated_at = NOW()
             WHERE id = @Id
            """,
            new
            {
                Id = childProfileId, insert.FirstName, insert.MiddleName, insert.LastName, insert.DateOfBirth,
                insert.Gender, insert.PhotoUrl, insert.PreviousSchool, insert.MedicalInfo
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<ChildReportCardRow>> GetReportCardsAsync(Guid childProfileId, CancellationToken cancellationToken)
    {
        return QueryAsync<ChildReportCardRow>(
            """
            SELECT rc.id, t.name AS Term, ay.name AS AcademicYear, sch.name AS SchoolName,
                   rc.status, rc.published_at AS PublishedAt
            FROM students st
            JOIN report_cards rc ON rc.student_id = st.id AND rc.status = 'published'
            JOIN schools sch ON sch.id = rc.school_id
            LEFT JOIN terms t ON t.id = rc.term_id
            LEFT JOIN academic_years ay ON ay.id = t.academic_year_id
            WHERE st.child_profile_id = @ChildProfileId
            ORDER BY rc.published_at DESC NULLS LAST
            """,
            new { ChildProfileId = childProfileId }, cancellationToken);
    }

    public Task<IReadOnlyList<ChildCaScoreRow>> GetCaScoresAsync(Guid childProfileId, Guid? termId, CancellationToken cancellationToken)
    {
        return QueryAsync<ChildCaScoreRow>(
            """
            SELECT sub.name AS SubjectName, gr.assessment_type AS AssessmentType, e.score AS Score,
                   gr.max_score AS MaxScore, gr.term_id AS TermId
            FROM students st
            JOIN grade_entries e ON e.student_id = st.id
            JOIN grade_records gr ON gr.id = e.grade_record_id AND gr.status = 'published'
            JOIN subjects sub ON sub.id = gr.subject_id
            WHERE st.child_profile_id = @ChildProfileId AND st.status = 'active'
              AND (@TermId IS NULL OR gr.term_id = @TermId)
            ORDER BY sub.name, gr.assessment_type
            """,
            new { ChildProfileId = childProfileId, TermId = termId }, cancellationToken);
    }

    public Task<IReadOnlyList<ChildAttendanceRow>> GetAttendanceAsync(Guid childProfileId, CancellationToken cancellationToken)
    {
        return QueryAsync<ChildAttendanceRow>(
            """
            SELECT t.name AS Term,
                   COUNT(*) FILTER (WHERE m.status = 'present')::int AS PresentDays,
                   COUNT(*) FILTER (WHERE m.status = 'absent')::int  AS AbsentDays,
                   COUNT(*) FILTER (WHERE m.status = 'late')::int     AS LateDays,
                   COUNT(*)::int AS TotalDays
            FROM students st
            JOIN attendance_marks m ON m.student_id = st.id
            JOIN attendance_records r ON r.id = m.attendance_record_id
            LEFT JOIN terms t ON t.id = r.term_id
            WHERE st.child_profile_id = @ChildProfileId AND st.status = 'active'
            GROUP BY t.name
            ORDER BY t.name
            """,
            new { ChildProfileId = childProfileId }, cancellationToken);
    }
}
