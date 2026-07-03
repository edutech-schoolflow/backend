using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Persistence;
using Npgsql;

namespace EduTech.Students.Admissions;

/// <summary>School-side admissions — tenant-scoped (every query binds @SchoolId).</summary>
internal interface ISchoolApplicationRepository
{
    Task<IReadOnlyList<ApplicationRow>> ListAsync(string? status, CancellationToken cancellationToken);
    Task<ApplicationRow?> GetAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<string?> GetStatusAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<bool> ClassExistsAsync(Guid classId, CancellationToken cancellationToken);
    Task<bool> ArmInClassAsync(Guid classArmId, Guid classId, CancellationToken cancellationToken);

    Task<int> ScheduleExamAsync(Guid applicationId, ApplicationStatus from, DateOnly? examDate, string? examTime,
        string? examVenue, string? examInstructions, CancellationToken cancellationToken);
    Task<int> RecordAssessmentAsync(Guid applicationId, string? rating, string? notes, CancellationToken cancellationToken);
    Task<int> RejectAsync(Guid applicationId, ApplicationStatus from, string? reason, CancellationToken cancellationToken);

    /// <summary>Admit: create a thin students enrollment from the child + mark the application admitted.</summary>
    Task<string> AdmitAsync(Guid applicationId, ApplicationStatus from, Guid classId, Guid? classArmId, CancellationToken cancellationToken);

    /// <summary>Parent phone + child name for a post-decision SMS.</summary>
    Task<ApplicationNotifyRow?> GetNotifyTargetAsync(Guid applicationId, CancellationToken cancellationToken);
}

internal sealed class ApplicationNotifyRow
{
    public string Phone { get; init; } = string.Empty;
    public string ChildName { get; init; } = string.Empty;
}

internal sealed class SchoolApplicationRepository : TenantRepository, ISchoolApplicationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SchoolApplicationRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<IReadOnlyList<ApplicationRow>> ListAsync(string? status, CancellationToken cancellationToken)
    {
        return QueryAsync<ApplicationRow>(
            $"""
            SELECT {ApplicationSql.Columns} {ApplicationSql.From}
            WHERE a.school_id = @SchoolId AND (@Status IS NULL OR a.status = @Status)
            ORDER BY a.created_at DESC
            """,
            TenantParameters(new { Status = status }), cancellationToken);
    }

    public Task<ApplicationRow?> GetAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ApplicationRow>(
            $"SELECT {ApplicationSql.Columns} {ApplicationSql.From} WHERE a.id = @Id AND a.school_id = @SchoolId",
            TenantParameters(new { Id = applicationId }), cancellationToken);
    }

    public Task<string?> GetStatusAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string?>(
            "SELECT status FROM applications WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = applicationId }), cancellationToken);
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

    public Task<int> ScheduleExamAsync(Guid applicationId, ApplicationStatus from, DateOnly? examDate, string? examTime,
        string? examVenue, string? examInstructions, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE applications
               SET exam_date = @ExamDate, exam_time = @ExamTime, exam_venue = @ExamVenue,
                   exam_instructions = @ExamInstructions, status = 'exam_scheduled', updated_at = NOW()
             WHERE id = @Id AND school_id = @SchoolId AND status = @From
            """,
            TenantParameters(new
            {
                Id = applicationId, From = SnakeCaseEnum.ToWire(from), ExamDate = examDate,
                ExamTime = examTime, ExamVenue = examVenue, ExamInstructions = examInstructions
            }),
            cancellationToken);
    }

    public Task<int> RecordAssessmentAsync(Guid applicationId, string? rating, string? notes, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE applications SET assessment_rating = @Rating, assessment_notes = @Notes, updated_at = NOW()
            WHERE id = @Id AND school_id = @SchoolId AND status IN ('under_review', 'exam_scheduled')
            """,
            TenantParameters(new { Id = applicationId, Rating = rating, Notes = notes }), cancellationToken);
    }

    public Task<int> RejectAsync(Guid applicationId, ApplicationStatus from, string? reason, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE applications SET status = 'rejected', rejection_reason = @Reason, updated_at = NOW()
            WHERE id = @Id AND school_id = @SchoolId AND status = @From
            """,
            TenantParameters(new { Id = applicationId, From = SnakeCaseEnum.ToWire(from), Reason = reason }),
            cancellationToken);
    }

    public async Task<string> AdmitAsync(Guid applicationId, ApplicationStatus from, Guid classId, Guid? classArmId,
        CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        System.Data.IDbTransaction tx = transaction.Transaction;

        Guid? childProfileId = await QuerySingleOrDefaultAsync<Guid?>(
            "SELECT child_profile_id FROM applications WHERE id = @Id AND school_id = @SchoolId AND status = @From",
            TenantParameters(new { Id = applicationId, From = SnakeCaseEnum.ToWire(from) }), cancellationToken, tx);
        if (childProfileId is not Guid childId)
        {
            throw new AppErrorException("Application status changed, please retry.", 409, ErrorCodes.Conflict);
        }

        string? subdomain = await QuerySingleOrDefaultAsync<string>(
            "SELECT subdomain FROM schools WHERE id = @SchoolId", TenantParameters(), cancellationToken, tx);
        string code = string.IsNullOrWhiteSpace(subdomain) ? "SCH" : subdomain.ToUpperInvariant();
        int existing = await ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM students WHERE school_id = @SchoolId", TenantParameters(), cancellationToken, tx);
        string admissionNumber = $"{code}/{DateTime.UtcNow.Year}/{existing + 1:D3}";

        Guid studentId;
        try
        {
            studentId = await ExecuteScalarAsync<Guid>(
                """
                INSERT INTO students (school_id, child_profile_id, class_id, class_arm_id, admission_number, status, enrolled_at)
                VALUES (@SchoolId, @ChildProfileId, @ClassId, @ClassArmId, @AdmissionNumber, 'active', NOW())
                RETURNING id
                """,
                TenantParameters(new { ChildProfileId = childId, ClassId = classId, ClassArmId = classArmId, AdmissionNumber = admissionNumber }),
                cancellationToken, tx);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // uq_one_active_enrollment: the child is already an active student somewhere else.
            throw new AppErrorException(
                "This child already has an active enrollment elsewhere; cross-school transfer isn't supported yet.",
                409, ErrorCodes.Conflict,
                logReason: $"Admit blocked by uq_one_active_enrollment for child {childId}.");
        }

        // Open the student's enrollment for the current session — the durable history behind alumni and
        // past-session rosters. academic_year_id is null when no current session is set yet.
        await ExecuteAsync(
            """
            INSERT INTO student_enrollments (school_id, student_id, academic_year_id, class_id, class_arm_id, outcome)
            SELECT @SchoolId, @StudentId,
                   (SELECT id FROM academic_years WHERE school_id = @SchoolId AND is_current = TRUE LIMIT 1),
                   @ClassId, @ClassArmId, 'enrolled'
            """,
            TenantParameters(new { StudentId = studentId, ClassId = classId, ClassArmId = classArmId }),
            cancellationToken, tx);

        await ExecuteAsync(
            """
            UPDATE applications
               SET status = 'admitted', admitted_student_id = @StudentId, admission_number = @AdmissionNumber, updated_at = NOW()
             WHERE id = @Id AND school_id = @SchoolId
            """,
            TenantParameters(new { Id = applicationId, StudentId = studentId, AdmissionNumber = admissionNumber }),
            cancellationToken, tx);

        await transaction.CommitAsync(cancellationToken);
        return admissionNumber;
    }

    public Task<ApplicationNotifyRow?> GetNotifyTargetAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ApplicationNotifyRow>(
            """
            SELECT p.phone AS Phone, concat_ws(' ', cp.first_name, cp.last_name) AS ChildName
            FROM applications a
            JOIN parents p ON p.id = a.parent_id
            JOIN child_profiles cp ON cp.id = a.child_profile_id
            WHERE a.id = @Id AND a.school_id = @SchoolId
            """,
            TenantParameters(new { Id = applicationId }), cancellationToken);
    }
}
