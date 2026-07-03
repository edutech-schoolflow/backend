using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Students.Classes;

internal interface IClassRepository
{
    Task<IReadOnlyList<SchoolClassRow>> ListClassesAsync(CancellationToken cancellationToken);
    Task<Guid> CreateClassWithArmsAsync(string name, ClassLevel level, int order,
        IReadOnlyList<(string Arm, Guid? TeacherAffiliationId)> arms, CancellationToken cancellationToken);
    Task<bool> ClassExistsAsync(Guid classId, CancellationToken cancellationToken);
    Task<bool> ClassNameExistsAsync(string name, CancellationToken cancellationToken);
    Task<SchoolClassRow?> GetClassAsync(Guid classId, CancellationToken cancellationToken);
    Task SetClassLevelTeacherAsync(Guid classId, Guid? teacherAffiliationId, CancellationToken cancellationToken);
    Task<ClassDependentsRow> GetDependentsAsync(Guid classId, CancellationToken cancellationToken);
    Task DeleteClassAsync(Guid classId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClassArmRow>> ListArmsAsync(Guid classId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubjectTeacherRow>> ListSubjectTeachersForClassAsync(Guid classId, CancellationToken cancellationToken);

    Task<bool> ArmExistsAsync(Guid armId, CancellationToken cancellationToken);
    Task<bool> ArmNameExistsAsync(Guid classId, string arm, CancellationToken cancellationToken);
    Task<Guid> AddArmAsync(Guid classId, string arm, Guid? teacherAffiliationId, CancellationToken cancellationToken);
    Task SetClassTeacherAsync(Guid armId, Guid? teacherAffiliationId, CancellationToken cancellationToken);
    Task<Guid> AddSubjectTeacherAsync(Guid armId, Guid teacherAffiliationId, string subject, CancellationToken cancellationToken);
    Task RemoveSubjectTeacherAsync(Guid subjectTeacherId, CancellationToken cancellationToken);

    /// <summary>True if the affiliation is an active staff member at the current school.</summary>
    Task<bool> AffiliationActiveAsync(Guid affiliationId, CancellationToken cancellationToken);
}

internal sealed class SchoolClassRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;   // snake_case in DB; service maps to ClassLevel
    public int Order { get; init; }
    public int ArmsCount { get; init; }
    public int StudentsCount { get; init; }
    public string? TeacherNames { get; init; }   // comma-joined; split in the service
    public Guid? ClassTeacherAffiliationId { get; init; }   // the class's own teacher (arm-less classes)
    public string? ClassTeacherName { get; init; }
}

/// <summary>Counts of records that block a class from being hard-deleted.</summary>
internal sealed class ClassDependentsRow
{
    public int Students { get; init; }
    public int Enrollments { get; init; }   // session-scoped history rows (incl. alumni)
    public int FeeTypes { get; init; }
    public int Attendance { get; init; }
    public int Grades { get; init; }
    public int Subjects { get; init; }

    public bool HasAny =>
        Students > 0 || Enrollments > 0 || FeeTypes > 0 || Attendance > 0 || Grades > 0 || Subjects > 0;
}

internal sealed class ClassArmRow
{
    public Guid Id { get; init; }
    public Guid ClassId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public string Arm { get; init; } = string.Empty;
    public Guid? ClassTeacherAffiliationId { get; init; }
    public string? ClassTeacherName { get; init; }
    public int StudentsCount { get; init; }
}

internal sealed class SubjectTeacherRow
{
    public Guid Id { get; init; }
    public Guid ClassArmId { get; init; }
    public Guid TeacherAffiliationId { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string TeacherName { get; init; } = string.Empty;
}

internal sealed class ClassRepository : TenantRepository, IClassRepository
{
    // Composed teacher name from the affiliation -> staff_users.
    private const string TeacherNameSql =
        "concat_ws(' ', su.first_name, su.middle_name, su.last_name)";

    private readonly IDbConnectionFactory _connectionFactory;

    public ClassRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
        _connectionFactory = connectionFactory;
    }

    // The class card shows every class teacher: the class's own teacher (arm-less classes) plus each
    // arm's teacher (split classes), de-duplicated.
    private const string ClassColumnsSql =
        """
        c.id, c.name, c.level, c.display_order AS "Order",
        (SELECT COUNT(*) FROM class_arms a WHERE a.class_id = c.id) AS ArmsCount,
        (SELECT COUNT(*) FROM students s WHERE s.class_id = c.id AND s.status = 'active') AS StudentsCount,
        c.class_teacher_affiliation_id AS ClassTeacherAffiliationId,
        concat_ws(' ', csu.first_name, csu.middle_name, csu.last_name) AS ClassTeacherName,
        (SELECT string_agg(name, ',') FROM (
            SELECT concat_ws(' ', su.first_name, su.middle_name, su.last_name) AS name
            FROM class_arms a
            JOIN staff_affiliations aff ON aff.id = a.class_teacher_affiliation_id
            JOIN staff_users su ON su.id = aff.staff_user_id
            WHERE a.class_id = c.id
            UNION
            SELECT concat_ws(' ', csu.first_name, csu.middle_name, csu.last_name)
            WHERE c.class_teacher_affiliation_id IS NOT NULL
        ) t) AS TeacherNames
        """;

    private const string ClassFromSql =
        """
        FROM classes c
        LEFT JOIN staff_affiliations caff ON caff.id = c.class_teacher_affiliation_id
        LEFT JOIN staff_users csu ON csu.id = caff.staff_user_id
        """;

    public Task<IReadOnlyList<SchoolClassRow>> ListClassesAsync(CancellationToken cancellationToken)
    {
        return QueryAsync<SchoolClassRow>(
            $"""
            SELECT {ClassColumnsSql}
            {ClassFromSql}
            WHERE c.school_id = @SchoolId
            ORDER BY c.display_order, c.name
            """,
            TenantParameters(), cancellationToken);
    }

    public Task<SchoolClassRow?> GetClassAsync(Guid classId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<SchoolClassRow>(
            $"SELECT {ClassColumnsSql} {ClassFromSql} WHERE c.id = @Id AND c.school_id = @SchoolId",
            TenantParameters(new { Id = classId }), cancellationToken);
    }

    public Task SetClassLevelTeacherAsync(Guid classId, Guid? teacherAffiliationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE classes SET class_teacher_affiliation_id = @Teacher, updated_at = NOW() WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = classId, Teacher = teacherAffiliationId }), cancellationToken);
    }

    public async Task<Guid> CreateClassWithArmsAsync(string name, ClassLevel level, int order,
        IReadOnlyList<(string Arm, Guid? TeacherAffiliationId)> arms, CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);

        Guid classId = await ExecuteScalarAsync<Guid>(
            "INSERT INTO classes (school_id, name, level, display_order) VALUES (@SchoolId, @Name, @Level, @Order) RETURNING id",
            TenantParameters(new { Name = name, Level = SnakeCaseEnum.ToWire(level), Order = order }), cancellationToken, transaction.Transaction);

        foreach ((string arm, Guid? teacher) in arms)
        {
            await ExecuteAsync(
                """
                INSERT INTO class_arms (school_id, class_id, arm, class_teacher_affiliation_id)
                VALUES (@SchoolId, @ClassId, @Arm, @Teacher)
                """,
                TenantParameters(new { ClassId = classId, Arm = arm, Teacher = teacher }),
                cancellationToken, transaction.Transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        return classId;
    }

    public async Task<bool> ClassExistsAsync(Guid classId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM classes WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = classId }), cancellationToken) > 0;
    }

    public async Task<bool> ClassNameExistsAsync(string name, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM classes WHERE school_id = @SchoolId AND lower(name) = lower(@Name)",
            TenantParameters(new { Name = name }), cancellationToken) > 0;
    }

    public async Task<ClassDependentsRow> GetDependentsAsync(Guid classId, CancellationToken cancellationToken)
    {
        return await QuerySingleOrDefaultAsync<ClassDependentsRow>(
            """
            SELECT
              (SELECT COUNT(*) FROM students s
                 WHERE s.class_id = @Id AND s.school_id = @SchoolId)::int AS Students,
              (SELECT COUNT(*) FROM student_enrollments e
                 WHERE e.class_id = @Id AND e.school_id = @SchoolId)::int AS Enrollments,
              (SELECT COUNT(DISTINCT ftc.fee_type_id) FROM fee_type_classes ftc
                 WHERE ftc.class_id = @Id)::int AS FeeTypes,
              (SELECT COUNT(*) FROM attendance_records r
                 WHERE r.class_id = @Id AND r.school_id = @SchoolId)::int AS Attendance,
              (SELECT COUNT(*) FROM grade_records gr
                 JOIN class_arms a ON a.id = gr.class_arm_id
                 WHERE a.class_id = @Id AND gr.school_id = @SchoolId)::int AS Grades,
              (SELECT COUNT(*) FROM subjects sub
                 WHERE sub.class_id = @Id AND sub.school_id = @SchoolId)::int AS Subjects
            """,
            TenantParameters(new { Id = classId }), cancellationToken) ?? new ClassDependentsRow();
    }

    public Task DeleteClassAsync(Guid classId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "DELETE FROM classes WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = classId }), cancellationToken);
    }

    public Task<IReadOnlyList<ClassArmRow>> ListArmsAsync(Guid classId, CancellationToken cancellationToken)
    {
        return QueryAsync<ClassArmRow>(
            $"""
            SELECT a.id, a.class_id AS ClassId, c.name AS ClassName, a.arm,
                   a.class_teacher_affiliation_id AS ClassTeacherAffiliationId,
                   {TeacherNameSql} AS ClassTeacherName,
                   (SELECT COUNT(*) FROM students s WHERE s.class_arm_id = a.id AND s.status = 'active') AS StudentsCount
            FROM class_arms a
            JOIN classes c ON c.id = a.class_id
            LEFT JOIN staff_affiliations aff ON aff.id = a.class_teacher_affiliation_id
            LEFT JOIN staff_users su ON su.id = aff.staff_user_id
            WHERE a.school_id = @SchoolId AND a.class_id = @ClassId
            ORDER BY a.arm
            """,
            TenantParameters(new { ClassId = classId }), cancellationToken);
    }

    public Task<IReadOnlyList<SubjectTeacherRow>> ListSubjectTeachersForClassAsync(Guid classId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<SubjectTeacherRow>(
            $"""
            SELECT st.id, st.class_arm_id AS ClassArmId, st.teacher_affiliation_id AS TeacherAffiliationId,
                   st.subject, {TeacherNameSql} AS TeacherName
            FROM class_subject_teachers st
            JOIN class_arms a ON a.id = st.class_arm_id
            JOIN staff_affiliations aff ON aff.id = st.teacher_affiliation_id
            JOIN staff_users su ON su.id = aff.staff_user_id
            WHERE st.school_id = @SchoolId AND a.class_id = @ClassId
            ORDER BY st.subject
            """,
            TenantParameters(new { ClassId = classId }), cancellationToken);
    }

    public async Task<bool> ArmExistsAsync(Guid armId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM class_arms WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = armId }), cancellationToken) > 0;
    }

    public async Task<bool> ArmNameExistsAsync(Guid classId, string arm, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM class_arms
            WHERE class_id = @ClassId AND school_id = @SchoolId AND lower(arm) = lower(@Arm)
            """,
            TenantParameters(new { ClassId = classId, Arm = arm }), cancellationToken) > 0;
    }

    public Task<Guid> AddArmAsync(Guid classId, string arm, Guid? teacherAffiliationId,
        CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO class_arms (school_id, class_id, arm, class_teacher_affiliation_id)
            VALUES (@SchoolId, @ClassId, @Arm, @Teacher)
            RETURNING id
            """,
            TenantParameters(new { ClassId = classId, Arm = arm, Teacher = teacherAffiliationId }),
            cancellationToken);
    }

    public Task SetClassTeacherAsync(Guid armId, Guid? teacherAffiliationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE class_arms SET class_teacher_affiliation_id = @Teacher, updated_at = NOW() WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = armId, Teacher = teacherAffiliationId }), cancellationToken);
    }

    public Task<Guid> AddSubjectTeacherAsync(Guid armId, Guid teacherAffiliationId, string subject,
        CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO class_subject_teachers (school_id, class_arm_id, teacher_affiliation_id, subject)
            VALUES (@SchoolId, @ArmId, @Teacher, @Subject)
            ON CONFLICT (class_arm_id, teacher_affiliation_id, subject) DO UPDATE SET subject = EXCLUDED.subject
            RETURNING id
            """,
            TenantParameters(new { ArmId = armId, Teacher = teacherAffiliationId, Subject = subject }),
            cancellationToken);
    }

    public Task RemoveSubjectTeacherAsync(Guid subjectTeacherId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "DELETE FROM class_subject_teachers WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = subjectTeacherId }), cancellationToken);
    }

    public async Task<bool> AffiliationActiveAsync(Guid affiliationId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM staff_affiliations WHERE id = @Id AND school_id = @SchoolId AND status = 'active'",
            TenantParameters(new { Id = affiliationId }), cancellationToken) > 0;
    }
}
