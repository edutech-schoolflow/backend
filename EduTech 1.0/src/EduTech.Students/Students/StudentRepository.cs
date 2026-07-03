using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Students.Students;

internal interface IStudentRepository
{
    Task<(IReadOnlyList<StudentRow> Rows, int Total)> ListAsync(Guid? classId, StudentStatus? status,
        int offset, int limit, CancellationToken cancellationToken);
    Task<StudentRow?> GetAsync(Guid studentId, CancellationToken cancellationToken);

    /// <summary>The student's guardians = linked parent accounts UNION extra (non-account) contacts.</summary>
    Task<IReadOnlyList<GuardianRow>> GetGuardiansAsync(Guid studentId, CancellationToken cancellationToken);

    /// <summary>
    /// School-add: resolve/create the primary parent (by phone, <c>pending</c>), create the global
    /// child_profile + parent_children link, then a THIN students (enrollment) row. Extra guardians
    /// become non-account guardian_contacts.
    /// </summary>
    Task<(Guid Id, string AdmissionNumber)> CreateAsync(StudentInsert student,
        IReadOnlyList<GuardianDto> extraGuardians, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid studentId, CancellationToken cancellationToken);
    Task<bool> ClassArmExistsAsync(Guid classArmId, CancellationToken cancellationToken);
    Task<bool> ClassExistsAsync(Guid classId, CancellationToken cancellationToken);
    Task<bool> ArmInClassAsync(Guid classArmId, Guid classId, CancellationToken cancellationToken);

    /// <summary>Replace the student's EXTRA (non-account) guardian contacts; parent links are untouched.</summary>
    Task ReplaceGuardiansAsync(Guid studentId, IReadOnlyList<GuardianDto> guardians, CancellationToken cancellationToken);

    /// <summary>The student's current status (snake_case string), or null if no such student here.</summary>
    Task<string?> GetStatusAsync(Guid studentId, CancellationToken cancellationToken);

    /// <summary>Race-safe status change: flips only if still in <paramref name="from"/>. Returns rows affected.</summary>
    Task<int> SetStatusIfAsync(Guid studentId, StudentStatus from, StudentStatus to, CancellationToken cancellationToken);

    Task SetClassArmAsync(Guid studentId, Guid classArmId, CancellationToken cancellationToken);

    Task<bool> YearExistsAsync(Guid academicYearId, CancellationToken cancellationToken);

    /// <summary>True if the target session is later than the school's current session (or none is set yet).</summary>
    Task<bool> IsSessionForwardAsync(Guid targetAcademicYearId, CancellationToken cancellationToken);

    /// <summary>
    /// End-of-session promotion (single transaction): for each student, close the open enrollment with its
    /// outcome, then either graduate them (status graduated, no next enrollment) or open a new enrollment in
    /// the target session/class and move their current placement there.
    /// </summary>
    Task PromoteStudentsAsync(Guid targetAcademicYearId, IReadOnlyList<PromotionCommand> commands,
        CancellationToken cancellationToken);
}

/// <summary>A validated, ready-to-apply promotion for one student.</summary>
internal sealed class PromotionCommand
{
    public required Guid StudentId { get; init; }
    public required string Outcome { get; init; }        // promoted | repeated | graduated (snake_case)
    public required bool Graduate { get; init; }
    public Guid? TargetClassId { get; init; }            // set for promote/repeat
    public Guid? TargetClassArmId { get; init; }
}

internal sealed class ParentLink
{
    public required string Phone { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Relationship { get; init; }   // mother | father | guardian
}

internal sealed class StudentInsert
{
    // child bio -> child_profiles
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public required DateOnly DateOfBirth { get; init; }
    public required Gender Gender { get; init; }
    public string? PhotoUrl { get; init; }
    public string? PreviousSchool { get; init; }
    public string? MedicalNotes { get; init; }
    // enrollment
    public required Guid ClassId { get; init; }    // the class (required)
    public Guid? ClassArmId { get; init; }         // optional stream within the class
    // primary guardian -> parent account
    public required ParentLink Parent { get; init; }
}

internal sealed class StudentRow
{
    public Guid Id { get; init; }
    public Guid ChildProfileId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public DateOnly DateOfBirth { get; init; }
    public string Gender { get; init; } = string.Empty;   // snake_case in DB; service maps to Gender
    public string? PhotoUrl { get; init; }
    public string? PreviousSchool { get; init; }
    public string? MedicalNotes { get; init; }
    public string? AdmissionNumber { get; init; }
    public Guid? ClassArmId { get; init; }
    public Guid? ClassId { get; init; }                   // resolved from the arm
    public string? ClassName { get; init; }
    public string? Arm { get; init; }                     // "" for a single/default arm
    public string Status { get; init; } = string.Empty;   // snake_case in DB; service maps to StudentStatus
    public DateTime CreatedAt { get; init; }
}

internal sealed class GuardianRow
{
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
    public string? Email { get; init; }
}

internal sealed class StudentRepository : TenantRepository, IStudentRepository
{
    // Bio now lives on the global child_profile; a student is a thin enrollment. Read via the join.
    private const string StudentColumns =
        "s.id, s.child_profile_id AS ChildProfileId, cp.first_name AS FirstName, cp.middle_name AS MiddleName, " +
        "cp.last_name AS LastName, cp.date_of_birth AS DateOfBirth, cp.gender, cp.photo_url AS PhotoUrl, " +
        "cp.previous_school AS PreviousSchool, cp.medical_info AS MedicalNotes, s.admission_number AS AdmissionNumber, " +
        "s.class_arm_id AS ClassArmId, s.class_id AS ClassId, cl.name AS ClassName, ca.arm AS Arm, " +
        "s.status, s.created_at AS CreatedAt";

    private const string StudentFrom =
        "FROM students s JOIN child_profiles cp ON cp.id = s.child_profile_id " +
        "LEFT JOIN classes cl ON cl.id = s.class_id " +
        "LEFT JOIN class_arms ca ON ca.id = s.class_arm_id";

    private readonly IDbConnectionFactory _connectionFactory;

    public StudentRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<(IReadOnlyList<StudentRow> Rows, int Total)> ListAsync(Guid? classId, StudentStatus? status,
        int offset, int limit, CancellationToken cancellationToken)
    {
        const string filter =
            "WHERE s.school_id = @SchoolId AND (@ClassId IS NULL OR s.class_id = @ClassId) " +
            "AND (@Status IS NULL OR s.status = @Status)";

        string? statusFilter = status.HasValue ? SnakeCaseEnum.ToWire(status.Value) : null;

        int total = await ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) {StudentFrom} {filter}",
            TenantParameters(new { ClassId = classId, Status = statusFilter }), cancellationToken);

        IReadOnlyList<StudentRow> rows = await QueryAsync<StudentRow>(
            $"SELECT {StudentColumns} {StudentFrom} {filter} ORDER BY cp.last_name, cp.first_name LIMIT @Limit OFFSET @Offset",
            TenantParameters(new { ClassId = classId, Status = statusFilter, Limit = limit, Offset = offset }),
            cancellationToken);

        return (rows, total);
    }

    public Task<StudentRow?> GetAsync(Guid studentId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<StudentRow>(
            $"SELECT {StudentColumns} {StudentFrom} WHERE s.id = @Id AND s.school_id = @SchoolId",
            TenantParameters(new { Id = studentId }), cancellationToken);
    }

    public Task<IReadOnlyList<GuardianRow>> GetGuardiansAsync(Guid studentId, CancellationToken cancellationToken)
    {
        // Guardians = linked parent ACCOUNTS (via parent_children) UNION extra non-account contacts.
        return QueryAsync<GuardianRow>(
            """
            SELECT concat_ws(' ', p.first_name, p.last_name) AS Name, p.phone AS Phone,
                   COALESCE(pc.relationship, '') AS Relationship, p.email AS Email
            FROM students s
            JOIN parent_children pc ON pc.child_profile_id = s.child_profile_id
            JOIN parents p ON p.id = pc.parent_id
            WHERE s.id = @Id AND s.school_id = @SchoolId
            UNION ALL
            SELECT gc.name, gc.phone, COALESCE(gc.relationship, ''), gc.email
            FROM students s
            JOIN guardian_contacts gc ON gc.child_profile_id = s.child_profile_id
            WHERE s.id = @Id AND s.school_id = @SchoolId
            """,
            TenantParameters(new { Id = studentId }), cancellationToken);
    }

    public async Task<(Guid Id, string AdmissionNumber)> CreateAsync(StudentInsert student,
        IReadOnlyList<GuardianDto> extraGuardians, CancellationToken cancellationToken)
    {
        string? subdomain = await QuerySingleOrDefaultAsync<string>(
            "SELECT subdomain FROM schools WHERE id = @SchoolId", TenantParameters(), cancellationToken);
        string code = string.IsNullOrWhiteSpace(subdomain) ? "SCH" : subdomain.ToUpperInvariant();

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        System.Data.IDbTransaction tx = transaction.Transaction;

        // 1. Resolve or create the primary parent (global table; keyed by phone, pending until claimed).
        Guid parentId = await QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM parents WHERE phone = @Phone",
            new { student.Parent.Phone }, cancellationToken, tx) ?? Guid.Empty;

        if (parentId == Guid.Empty)
        {
            parentId = await ExecuteScalarAsync<Guid>(
                """
                INSERT INTO parents (first_name, last_name, phone, status, phone_verified)
                VALUES (@FirstName, @LastName, @Phone, 'pending', FALSE)
                RETURNING id
                """,
                new
                {
                    FirstName = Nz(student.Parent.FirstName, "Guardian"),
                    LastName = Nz(student.Parent.LastName, Nz(student.Parent.FirstName, "Guardian")),
                    student.Parent.Phone
                },
                cancellationToken, tx);
        }

        // 2. Global child profile (owned by the parent).
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
                ParentId = parentId, student.FirstName, student.MiddleName, student.LastName,
                student.DateOfBirth, Gender = SnakeCaseEnum.ToWire(student.Gender), student.PhotoUrl,
                student.PreviousSchool, MedicalInfo = student.MedicalNotes
            },
            cancellationToken, tx);

        // 3. Guardian link (primary).
        await ExecuteAsync(
            """
            INSERT INTO parent_children (parent_id, child_profile_id, relationship, is_primary)
            VALUES (@ParentId, @ChildProfileId, @Relationship, TRUE)
            ON CONFLICT (parent_id, child_profile_id) DO NOTHING
            """,
            new { ParentId = parentId, ChildProfileId = childProfileId, student.Parent.Relationship },
            cancellationToken, tx);

        // 4. Thin enrollment row (admission number per school; known race, acceptable v1).
        int existing = await ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM students WHERE school_id = @SchoolId",
            TenantParameters(), cancellationToken, tx);
        string admissionNumber = $"{code}/{DateTime.UtcNow.Year}/{existing + 1:D3}";

        Guid id = await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO students (school_id, child_profile_id, class_id, class_arm_id, admission_number, status, enrolled_at)
            VALUES (@SchoolId, @ChildProfileId, @ClassId, @ClassArmId, @AdmissionNumber, 'active', NOW())
            RETURNING id
            """,
            TenantParameters(new { ChildProfileId = childProfileId, student.ClassId, student.ClassArmId, AdmissionNumber = admissionNumber }),
            cancellationToken, tx);

        // 5. Open the student's enrollment for the current session (durable history behind alumni /
        //    past-session rosters). academic_year_id is null when no current session is set yet.
        await InsertEnrollmentAsync(id, student.ClassId, student.ClassArmId, tx, cancellationToken);

        // 6. Extra (non-account) guardian contacts.
        await InsertGuardianContactsAsync(childProfileId, extraGuardians, tx, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (id, admissionNumber);
    }

    /// <summary>Opens a student's enrollment for the school's current session (academic year), if one is set.</summary>
    private Task InsertEnrollmentAsync(Guid studentId, Guid classId, Guid? classArmId,
        System.Data.IDbTransaction tx, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO student_enrollments (school_id, student_id, academic_year_id, class_id, class_arm_id, outcome)
            SELECT @SchoolId, @StudentId,
                   (SELECT id FROM academic_years WHERE school_id = @SchoolId AND is_current = TRUE LIMIT 1),
                   @ClassId, @ClassArmId, 'enrolled'
            """,
            TenantParameters(new { StudentId = studentId, ClassId = classId, ClassArmId = classArmId }),
            cancellationToken, tx);
    }

    public async Task<bool> ExistsAsync(Guid studentId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM students WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = studentId }), cancellationToken) > 0;
    }

    public async Task<bool> ClassArmExistsAsync(Guid classArmId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM class_arms WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = classArmId }), cancellationToken) > 0;
    }

    public async Task<bool> ClassExistsAsync(Guid classId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM classes WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = classId }), cancellationToken) > 0;
    }

    public async Task<bool> ArmInClassAsync(Guid classArmId, Guid classId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM class_arms WHERE id = @Id AND class_id = @ClassId AND school_id = @SchoolId",
            TenantParameters(new { Id = classArmId, ClassId = classId }), cancellationToken) > 0;
    }

    public async Task<bool> YearExistsAsync(Guid academicYearId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM academic_years WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = academicYearId }), cancellationToken) > 0;
    }

    public async Task<bool> IsSessionForwardAsync(Guid targetAcademicYearId, CancellationToken cancellationToken)
    {
        // Forward = no current session yet, or the target session was created after the current one.
        // (A dedicated session order key would make this exact; created_at is the pragmatic proxy for now.)
        return await ExecuteScalarAsync<bool>(
            """
            SELECT CASE
              WHEN NOT EXISTS (SELECT 1 FROM academic_years WHERE school_id = @SchoolId AND is_current = TRUE)
                THEN TRUE
              ELSE COALESCE(
                (SELECT t.created_at FROM academic_years t WHERE t.id = @TargetId AND t.school_id = @SchoolId)
                > (SELECT c.created_at FROM academic_years c WHERE c.school_id = @SchoolId AND c.is_current = TRUE),
                FALSE)
            END
            """,
            TenantParameters(new { TargetId = targetAcademicYearId }), cancellationToken);
    }

    public async Task PromoteStudentsAsync(Guid targetAcademicYearId, IReadOnlyList<PromotionCommand> commands,
        CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        System.Data.IDbTransaction tx = transaction.Transaction;

        foreach (PromotionCommand c in commands)
        {
            // 1. Close the student's currently-open enrollment with its outcome.
            await ExecuteAsync(
                """
                UPDATE student_enrollments
                   SET outcome = @Outcome, ended_on = CURRENT_DATE, updated_at = NOW()
                 WHERE student_id = @StudentId AND school_id = @SchoolId AND ended_on IS NULL
                """,
                TenantParameters(new { c.StudentId, c.Outcome }), cancellationToken, tx);

            if (c.Graduate)
            {
                // 2a. Graduated → alumni. No next enrollment; class_id is kept for reference.
                await ExecuteAsync(
                    "UPDATE students SET status = 'graduated', updated_at = NOW() WHERE id = @StudentId AND school_id = @SchoolId",
                    TenantParameters(new { c.StudentId }), cancellationToken, tx);
                continue;
            }

            // 2b. Open the next session's enrollment (idempotent per student+session) and move the placement.
            await ExecuteAsync(
                """
                INSERT INTO student_enrollments (school_id, student_id, academic_year_id, class_id, class_arm_id, outcome)
                VALUES (@SchoolId, @StudentId, @YearId, @ClassId, @ArmId, 'enrolled')
                ON CONFLICT (student_id, academic_year_id) WHERE academic_year_id IS NOT NULL
                DO UPDATE SET class_id = EXCLUDED.class_id, class_arm_id = EXCLUDED.class_arm_id,
                              outcome = 'enrolled', ended_on = NULL, updated_at = NOW()
                """,
                TenantParameters(new { c.StudentId, YearId = targetAcademicYearId, ClassId = c.TargetClassId, ArmId = c.TargetClassArmId }),
                cancellationToken, tx);

            await ExecuteAsync(
                """
                UPDATE students SET class_id = @ClassId, class_arm_id = @ArmId, status = 'active', updated_at = NOW()
                 WHERE id = @StudentId AND school_id = @SchoolId
                """,
                TenantParameters(new { c.StudentId, ClassId = c.TargetClassId, ArmId = c.TargetClassArmId }),
                cancellationToken, tx);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReplaceGuardiansAsync(Guid studentId, IReadOnlyList<GuardianDto> guardians,
        CancellationToken cancellationToken)
    {
        Guid? childProfileId = await ExecuteScalarAsync<Guid?>(
            "SELECT child_profile_id FROM students WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = studentId }), cancellationToken);
        if (childProfileId is not Guid cpId)
        {
            return;
        }

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(
            "DELETE FROM guardian_contacts WHERE child_profile_id = @ChildProfileId",
            new { ChildProfileId = cpId }, cancellationToken, transaction.Transaction);
        await InsertGuardianContactsAsync(cpId, guardians, transaction.Transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<string?> GetStatusAsync(Guid studentId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string?>(
            "SELECT status FROM students WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = studentId }), cancellationToken);
    }

    public Task<int> SetStatusIfAsync(Guid studentId, StudentStatus from, StudentStatus to,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE students SET status = @To, updated_at = NOW() " +
            "WHERE id = @Id AND school_id = @SchoolId AND status = @From",
            TenantParameters(new { Id = studentId, From = SnakeCaseEnum.ToWire(from), To = SnakeCaseEnum.ToWire(to) }),
            cancellationToken);
    }

    public Task SetClassArmAsync(Guid studentId, Guid classArmId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE students SET class_arm_id = @ArmId, updated_at = NOW() WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = studentId, ArmId = classArmId }), cancellationToken);
    }

    private async Task InsertGuardianContactsAsync(Guid childProfileId, IReadOnlyList<GuardianDto> guardians,
        System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (GuardianDto g in guardians)
        {
            await ExecuteAsync(
                """
                INSERT INTO guardian_contacts (child_profile_id, name, phone, relationship, email)
                VALUES (@ChildProfileId, @Name, @Phone, @Relationship, @Email)
                """,
                new
                {
                    ChildProfileId = childProfileId, Name = g.Name.Trim(), Phone = g.Phone.Trim(),
                    Relationship = g.Relationship.Trim(),
                    Email = string.IsNullOrWhiteSpace(g.Email) ? null : g.Email.Trim()
                },
                cancellationToken, transaction);
        }
    }

    private static string Nz(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
