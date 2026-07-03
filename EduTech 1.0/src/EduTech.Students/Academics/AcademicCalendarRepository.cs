using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Students.Academics;

/// <summary>Per-school academic calendar (years + terms). Tenant-scoped: every query filters @SchoolId.</summary>
internal interface IAcademicCalendarRepository
{
    Task<IReadOnlyList<AcademicYearRow>> ListYearsAsync(CancellationToken cancellationToken);
    Task<(Guid Id, bool IsCurrent)> CreateYearAsync(string name, int? startsIn, CancellationToken cancellationToken);
    Task<bool> YearExistsAsync(Guid yearId, CancellationToken cancellationToken);
    Task<bool> YearNameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken);
    Task SetCurrentYearAsync(Guid yearId, CancellationToken cancellationToken);
    Task UpdateYearAsync(Guid yearId, string name, int? startsIn, CancellationToken cancellationToken);
    Task DeleteYearAsync(Guid yearId, CancellationToken cancellationToken);
    Task<YearDependentsRow> YearDependentsAsync(Guid yearId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TermRow>> ListTermsAsync(Guid? academicYearId, CancellationToken cancellationToken);
    Task<TermRow?> GetTermAsync(Guid termId, CancellationToken cancellationToken);
    Task<(Guid Id, bool IsCurrent)> CreateTermAsync(Guid academicYearId, Term name, DateOnly? startDate,
        DateOnly? endDate, CancellationToken cancellationToken);
    Task<bool> TermExistsAsync(Guid termId, CancellationToken cancellationToken);
    Task<bool> TermNameExistsAsync(Guid academicYearId, Term name, CancellationToken cancellationToken);
    Task UpdateTermDatesAsync(Guid termId, DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken);
    Task DeleteTermAsync(Guid termId, CancellationToken cancellationToken);
    Task<TermDependentsRow> TermDependentsAsync(Guid termId, CancellationToken cancellationToken);

    /// <summary>True if [start, end] overlaps any other dated term in the school (a school runs one term at a time).</summary>
    Task<bool> HasDateOverlapAsync(DateOnly start, DateOnly end, Guid? exceptTermId, CancellationToken cancellationToken);
    Task SetCurrentTermAsync(Guid termId, CancellationToken cancellationToken);
}

internal sealed class AcademicYearRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
}

internal sealed class TermRow
{
    public Guid Id { get; init; }
    public Guid AcademicYearId { get; init; }
    public string Name { get; init; } = string.Empty;   // snake_case in DB; service maps to Term
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public bool IsCurrent { get; init; }
}

/// <summary>INSERT ... RETURNING id + whether the row was auto-marked current.</summary>
internal sealed class CreatedCalendarRow
{
    public Guid Id { get; init; }
    public bool IsCurrent { get; init; }
}

/// <summary>What blocks a session from being deleted.</summary>
internal sealed class YearDependentsRow
{
    public int Terms { get; init; }
    public int Enrollments { get; init; }
    public bool HasAny => Terms > 0 || Enrollments > 0;
}

/// <summary>What blocks a term from being deleted (records that would otherwise cascade away).</summary>
internal sealed class TermDependentsRow
{
    public int FeeTypes { get; init; }
    public int Grades { get; init; }
    public int ReportCards { get; init; }
    public int Attendance { get; init; }
    public int Payments { get; init; }

    public bool HasAny => FeeTypes > 0 || Grades > 0 || ReportCards > 0 || Attendance > 0 || Payments > 0;
}

internal sealed class AcademicCalendarRepository : TenantRepository, IAcademicCalendarRepository
{
    public AcademicCalendarRepository(IDbConnectionFactory connectionFactory,
        IEduTechRequestContext requestContext) : base(connectionFactory, requestContext)
    {
    }

    public Task<IReadOnlyList<AcademicYearRow>> ListYearsAsync(CancellationToken cancellationToken)
    {
        return QueryAsync<AcademicYearRow>(
            """
            SELECT id, name, is_current AS IsCurrent FROM academic_years
            WHERE school_id = @SchoolId
            ORDER BY starts_in DESC NULLS LAST, name DESC
            """,
            TenantParameters(), cancellationToken);
    }

    public async Task<(Guid Id, bool IsCurrent)> CreateYearAsync(string name, int? startsIn,
        CancellationToken cancellationToken)
    {
        // The very first session a school creates becomes the current one (nothing else competes).
        CreatedCalendarRow row = await QuerySingleOrDefaultAsync<CreatedCalendarRow>(
            """
            INSERT INTO academic_years (school_id, name, starts_in, is_current)
            VALUES (@SchoolId, @Name, @StartsIn, NOT EXISTS (SELECT 1 FROM academic_years WHERE school_id = @SchoolId))
            RETURNING id AS Id, is_current AS IsCurrent
            """,
            TenantParameters(new { Name = name, StartsIn = startsIn }), cancellationToken)
            ?? throw new InvalidOperationException("Insert did not return the new academic year.");
        return (row.Id, row.IsCurrent);
    }

    public async Task<bool> YearExistsAsync(Guid yearId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM academic_years WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = yearId }), cancellationToken) > 0;
    }

    public async Task<bool> YearNameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM academic_years
            WHERE school_id = @SchoolId AND lower(name) = lower(@Name) AND (@ExceptId IS NULL OR id <> @ExceptId)
            """,
            TenantParameters(new { Name = name, ExceptId = exceptId }), cancellationToken) > 0;
    }

    public Task SetCurrentYearAsync(Guid yearId, CancellationToken cancellationToken)
    {
        // One current year per school. And the current term must live inside the current session, so any
        // current-term flag in a *different* session is cleared — the school then picks a term in the new one.
        return ExecuteAsync(
            """
            UPDATE academic_years SET is_current = (id = @Id), updated_at = NOW() WHERE school_id = @SchoolId;
            UPDATE terms SET is_current = FALSE
             WHERE school_id = @SchoolId AND is_current = TRUE AND academic_year_id <> @Id;
            """,
            TenantParameters(new { Id = yearId }), cancellationToken);
    }

    public Task UpdateYearAsync(Guid yearId, string name, int? startsIn, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE academic_years SET name = @Name, starts_in = @StartsIn, updated_at = NOW() WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = yearId, Name = name, StartsIn = startsIn }), cancellationToken);
    }

    public Task DeleteYearAsync(Guid yearId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "DELETE FROM academic_years WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = yearId }), cancellationToken);
    }

    public async Task<YearDependentsRow> YearDependentsAsync(Guid yearId, CancellationToken cancellationToken)
    {
        return await QuerySingleOrDefaultAsync<YearDependentsRow>(
            """
            SELECT
              (SELECT COUNT(*) FROM terms t WHERE t.academic_year_id = @Id AND t.school_id = @SchoolId)::int AS Terms,
              (SELECT COUNT(*) FROM student_enrollments e WHERE e.academic_year_id = @Id AND e.school_id = @SchoolId)::int AS Enrollments
            """,
            TenantParameters(new { Id = yearId }), cancellationToken) ?? new YearDependentsRow();
    }

    public Task<IReadOnlyList<TermRow>> ListTermsAsync(Guid? academicYearId, CancellationToken cancellationToken)
    {
        return QueryAsync<TermRow>(
            """
            SELECT id, academic_year_id AS AcademicYearId, name, start_date AS StartDate, end_date AS EndDate,
                   is_current AS IsCurrent
            FROM terms
            WHERE school_id = @SchoolId AND (@AcademicYearId IS NULL OR academic_year_id = @AcademicYearId)
            ORDER BY academic_year_id, name
            """,
            TenantParameters(new { AcademicYearId = academicYearId }), cancellationToken);
    }

    public Task<TermRow?> GetTermAsync(Guid termId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<TermRow>(
            """
            SELECT id, academic_year_id AS AcademicYearId, name, start_date AS StartDate, end_date AS EndDate,
                   is_current AS IsCurrent
            FROM terms WHERE id = @Id AND school_id = @SchoolId
            """,
            TenantParameters(new { Id = termId }), cancellationToken);
    }

    public async Task<(Guid Id, bool IsCurrent)> CreateTermAsync(Guid academicYearId, Term name,
        DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken)
    {
        // Auto-mark current only when it's the current session's term and the school has no current term yet
        // (onboarding: first session is current → its first term becomes the active term so fees/enrolment work).
        CreatedCalendarRow row = await QuerySingleOrDefaultAsync<CreatedCalendarRow>(
            """
            INSERT INTO terms (school_id, academic_year_id, name, start_date, end_date, is_current)
            VALUES (@SchoolId, @AcademicYearId, @Name, @StartDate, @EndDate,
                    COALESCE(
                      @AcademicYearId = (SELECT id FROM academic_years WHERE school_id = @SchoolId AND is_current = TRUE)
                      AND NOT EXISTS (SELECT 1 FROM terms WHERE school_id = @SchoolId AND is_current = TRUE),
                      FALSE))
            RETURNING id AS Id, is_current AS IsCurrent
            """,
            TenantParameters(new { AcademicYearId = academicYearId, Name = SnakeCaseEnum.ToWire(name), StartDate = startDate, EndDate = endDate }),
            cancellationToken)
            ?? throw new InvalidOperationException("Insert did not return the new term.");
        return (row.Id, row.IsCurrent);
    }

    public async Task<bool> TermExistsAsync(Guid termId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM terms WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = termId }), cancellationToken) > 0;
    }

    public async Task<bool> TermNameExistsAsync(Guid academicYearId, Term name, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM terms
            WHERE school_id = @SchoolId AND academic_year_id = @AcademicYearId AND name = @Name
            """,
            TenantParameters(new { AcademicYearId = academicYearId, Name = SnakeCaseEnum.ToWire(name) }),
            cancellationToken) > 0;
    }

    public Task UpdateTermDatesAsync(Guid termId, DateOnly? startDate, DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE terms SET start_date = @StartDate, end_date = @EndDate WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = termId, StartDate = startDate, EndDate = endDate }), cancellationToken);
    }

    public Task DeleteTermAsync(Guid termId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "DELETE FROM terms WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = termId }), cancellationToken);
    }

    public async Task<TermDependentsRow> TermDependentsAsync(Guid termId, CancellationToken cancellationToken)
    {
        return await QuerySingleOrDefaultAsync<TermDependentsRow>(
            """
            SELECT
              (SELECT COUNT(*) FROM fee_types ft WHERE ft.term_id = @Id AND ft.school_id = @SchoolId)::int AS FeeTypes,
              (SELECT COUNT(*) FROM grade_records gr WHERE gr.term_id = @Id AND gr.school_id = @SchoolId)::int AS Grades,
              (SELECT COUNT(*) FROM report_cards rc WHERE rc.term_id = @Id AND rc.school_id = @SchoolId)::int AS ReportCards,
              (SELECT COUNT(*) FROM attendance_records ar WHERE ar.term_id = @Id AND ar.school_id = @SchoolId)::int AS Attendance,
              (SELECT COUNT(*) FROM payments p WHERE p.term_id = @Id AND p.school_id = @SchoolId)::int AS Payments
            """,
            TenantParameters(new { Id = termId }), cancellationToken) ?? new TermDependentsRow();
    }

    public async Task<bool> HasDateOverlapAsync(DateOnly start, DateOnly end, Guid? exceptTermId,
        CancellationToken cancellationToken)
    {
        // Two closed intervals [a,b] and [c,d] overlap iff a <= d AND c <= b.
        return await ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM terms
            WHERE school_id = @SchoolId AND start_date IS NOT NULL AND end_date IS NOT NULL
              AND start_date <= @End AND @Start <= end_date
              AND (@ExceptId IS NULL OR id <> @ExceptId)
            """,
            TenantParameters(new { Start = start, End = end, ExceptId = exceptTermId }), cancellationToken) > 0;
    }

    public Task SetCurrentTermAsync(Guid termId, CancellationToken cancellationToken)
    {
        // One current term per school, and it must sit inside the current session — so making a term current
        // also makes its session the current session (real life: the active term is always in the active year).
        return ExecuteAsync(
            """
            UPDATE terms SET is_current = (id = @Id) WHERE school_id = @SchoolId;
            UPDATE academic_years SET is_current =
                     (id = (SELECT academic_year_id FROM terms WHERE id = @Id AND school_id = @SchoolId)),
                   updated_at = NOW()
             WHERE school_id = @SchoolId;
            """,
            TenantParameters(new { Id = termId }), cancellationToken);
    }
}
