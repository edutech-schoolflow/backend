using EduTech.Shared.Constants;
using EduTech.Shared.Persistence;

namespace EduTech.Students.Academics.Transition;

/// <summary>
/// Data access for the platform-wide calendar sweep. Unlike every request-path repository this one
/// is NOT tenant-scoped — the Hangfire job runs with no request context and iterates all schools —
/// so every method takes the school id explicitly. Public (with the job) so the API host can
/// register the recurring job.
/// </summary>
public interface ICalendarRollForwardRepository
{
    Task<IReadOnlyList<Guid>> ListSchoolIdsAsync(CancellationToken cancellationToken);
    Task<CalendarSnapshotRow> GetSnapshotAsync(Guid schoolId, CancellationToken cancellationToken);

    /// <summary>First-time setup: the session, its three default-dated terms, and the current markers.</summary>
    Task ProvisionCalendarAsync(Guid schoolId, int sessionStartYear, Term currentTerm,
        IReadOnlyList<(Term Term, DateOnly Start, DateOnly End)> termWindows, CancellationToken cancellationToken);

    /// <summary>The year named <paramref name="name"/>, created (NOT current) if missing.</summary>
    Task<Guid> EnsureYearAsync(Guid schoolId, string name, int startsIn, CancellationToken cancellationToken);

    Task<bool> TermExistsInYearAsync(Guid schoolId, Guid yearId, Term term, CancellationToken cancellationToken);

    /// <summary>Creates the term (NOT current) if missing; the school's confirm flips the pointers.</summary>
    Task PrepareTermInYearAsync(Guid schoolId, Guid yearId, Term term, (DateOnly Start, DateOnly End)? dates,
        CancellationToken cancellationToken);

    Task<string?> GetOwnerPhoneAsync(Guid schoolId, CancellationToken cancellationToken);
}

/// <summary>One school's calendar position, as seen by the daily sweep.</summary>
public sealed class CalendarSnapshotRow
{
    public bool HasYears { get; init; }
    public Guid? CurrentYearId { get; init; }
    public int? CurrentYearStartsIn { get; init; }
    public string? CurrentYearName { get; init; }
    public string? CurrentTermName { get; init; }
    public DateOnly? CurrentTermEndDate { get; init; }
}

internal sealed class CalendarRollForwardRepository : BaseRepository, ICalendarRollForwardRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CalendarRollForwardRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<IReadOnlyList<Guid>> ListSchoolIdsAsync(CancellationToken cancellationToken)
    {
        return QueryAsync<Guid>("SELECT id FROM schools", null, cancellationToken);
    }

    public async Task<CalendarSnapshotRow> GetSnapshotAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        bool hasYears = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM academic_years WHERE school_id = @SchoolId",
            new { SchoolId = schoolId }, cancellationToken) > 0;
        if (!hasYears)
        {
            return new CalendarSnapshotRow { HasYears = false };
        }

        CalendarSnapshotRow? current = await QuerySingleOrDefaultAsync<CalendarSnapshotRow>(
            """
            SELECT TRUE AS HasYears, y.id AS CurrentYearId, y.starts_in AS CurrentYearStartsIn,
                   y.name AS CurrentYearName, t.name AS CurrentTermName, t.end_date AS CurrentTermEndDate
            FROM terms t
            JOIN academic_years y ON y.id = t.academic_year_id
            WHERE t.school_id = @SchoolId AND t.is_current = TRUE
            LIMIT 1
            """,
            new { SchoolId = schoolId }, cancellationToken);

        return current ?? new CalendarSnapshotRow { HasYears = true };
    }

    public async Task ProvisionCalendarAsync(Guid schoolId, int sessionStartYear, Term currentTerm,
        IReadOnlyList<(Term Term, DateOnly Start, DateOnly End)> termWindows, CancellationToken cancellationToken)
    {
        string name = SessionName(sessionStartYear);

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);

        Guid yearId = await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO academic_years (school_id, name, starts_in, is_current)
            VALUES (@SchoolId, @Name, @StartsIn, TRUE)
            RETURNING id
            """,
            new { SchoolId = schoolId, Name = name, StartsIn = sessionStartYear },
            cancellationToken, transaction.Transaction);

        foreach ((Term term, DateOnly start, DateOnly end) in termWindows)
        {
            await ExecuteAsync(
                """
                INSERT INTO terms (school_id, academic_year_id, name, start_date, end_date, is_current)
                VALUES (@SchoolId, @YearId, @Name, @Start, @End, @IsCurrent)
                """,
                new
                {
                    SchoolId = schoolId, YearId = yearId, Name = SnakeCaseEnum.ToWire(term),
                    Start = start, End = end, IsCurrent = term == currentTerm
                },
                cancellationToken, transaction.Transaction);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid> EnsureYearAsync(Guid schoolId, string name, int startsIn, CancellationToken cancellationToken)
    {
        Guid? existing = await QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM academic_years WHERE school_id = @SchoolId AND name = @Name",
            new { SchoolId = schoolId, Name = name }, cancellationToken);
        if (existing is Guid id)
        {
            return id;
        }

        return await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO academic_years (school_id, name, starts_in, is_current)
            VALUES (@SchoolId, @Name, @StartsIn, FALSE)
            RETURNING id
            """,
            new { SchoolId = schoolId, Name = name, StartsIn = startsIn }, cancellationToken);
    }

    public async Task<bool> TermExistsInYearAsync(Guid schoolId, Guid yearId, Term term, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM terms
            WHERE school_id = @SchoolId AND academic_year_id = @YearId AND name = @Name
            """,
            new { SchoolId = schoolId, YearId = yearId, Name = SnakeCaseEnum.ToWire(term) }, cancellationToken) > 0;
    }

    public Task PrepareTermInYearAsync(Guid schoolId, Guid yearId, Term term, (DateOnly Start, DateOnly End)? dates,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO terms (school_id, academic_year_id, name, start_date, end_date, is_current)
            SELECT @SchoolId, @YearId, @Name, @Start, @End, FALSE
            WHERE NOT EXISTS (
                SELECT 1 FROM terms WHERE academic_year_id = @YearId AND name = @Name)
            """,
            new
            {
                SchoolId = schoolId, YearId = yearId, Name = SnakeCaseEnum.ToWire(term),
                Start = dates?.Start, End = dates?.End
            },
            cancellationToken);
    }

    public Task<string?> GetOwnerPhoneAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string?>(
            "SELECT phone FROM school_owners WHERE school_id = @SchoolId AND is_active = TRUE LIMIT 1",
            new { SchoolId = schoolId }, cancellationToken);
    }

    internal static string SessionName(int startYear) => $"{startYear}/{(startYear + 1) % 100:00}";
}
