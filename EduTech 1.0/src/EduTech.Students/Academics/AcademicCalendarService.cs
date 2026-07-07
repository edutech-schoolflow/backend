using System.Text.RegularExpressions;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Students.Academics.Domain;

namespace EduTech.Students.Academics;

public interface IAcademicCalendarService
{
    Task<IReadOnlyList<AcademicYearResponse>> ListYearsAsync(CancellationToken cancellationToken);
    Task<AcademicYearResponse> CreateYearAsync(CreateAcademicYearRequest request, CancellationToken cancellationToken);
    Task SetCurrentYearAsync(Guid yearId, CancellationToken cancellationToken);
    Task UpdateYearAsync(Guid yearId, UpdateAcademicYearRequest request, CancellationToken cancellationToken);
    Task DeleteYearAsync(Guid yearId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TermResponse>> ListTermsAsync(Guid? academicYearId, CancellationToken cancellationToken);
    Task<TermResponse> CreateTermAsync(CreateTermRequest request, CancellationToken cancellationToken);
    Task SetCurrentTermAsync(Guid termId, CancellationToken cancellationToken);
    Task UpdateTermDatesAsync(Guid termId, UpdateTermDatesRequest request, CancellationToken cancellationToken);
    Task DeleteTermAsync(Guid termId, CancellationToken cancellationToken);
}

internal sealed class AcademicCalendarService : IAcademicCalendarService
{
    private readonly IAcademicCalendarRepository _repository;

    public AcademicCalendarService(IAcademicCalendarRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<AcademicYearResponse>> ListYearsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AcademicYearRow> rows = await _repository.ListYearsAsync(cancellationToken);
        return rows.Select(r => MapAcademicYear(r)).ToList();
    }

    public async Task<AcademicYearResponse> CreateYearAsync(CreateAcademicYearRequest request,
        CancellationToken cancellationToken)
    {
        if (request.StartYear <= 0 || request.EndYear <= 0)
        {
            throw new AppErrorException("Session start and end years are required.", 400, ErrorCodes.ValidationError);
        }

        if (request.EndYear != request.StartYear + 1)
        {
            throw new AppErrorException("Session duration must be exactly 1 year.", 400, ErrorCodes.ValidationError);
        }

        string name = BuildSessionName(request.StartYear, request.EndYear);
        int? startsIn = request.StartYear;

        if (await _repository.YearNameExistsAsync(name, null, cancellationToken))
        {
            throw new AppErrorException($"A session named \"{name}\" already exists.", 409, ErrorCodes.Conflict);
        }

        (Guid id, bool isCurrent) = await _repository.CreateYearAsync(name, startsIn, cancellationToken);
        return new AcademicYearResponse { Id = id, Name = name, StartYear = request.StartYear, EndYear = request.EndYear, IsCurrent = isCurrent };
    }

    public async Task SetCurrentYearAsync(Guid yearId, CancellationToken cancellationToken)
    {
        if (!await _repository.YearExistsAsync(yearId, cancellationToken))
        {
            throw new AppErrorException("Academic year not found.", 404, ErrorCodes.NotFound);
        }

        await _repository.SetCurrentYearAsync(yearId, cancellationToken);
    }

    public async Task UpdateYearAsync(Guid yearId, UpdateAcademicYearRequest request, CancellationToken cancellationToken)
    {
        AcademicYearRow year = await _repository.GetYearAsync(yearId, cancellationToken)
            ?? throw new AppErrorException("Academic year not found.", 404, ErrorCodes.NotFound);

        // The aggregate owns the identity lock: a session that's ongoing or already has terms can't be
        // re-dated (it would misalign the terms/results/fees anchored to it).
        IReadOnlyList<TermRow> yearTerms = await _repository.ListTermsAsync(yearId, cancellationToken);
        AcademicSession session = BuildSession(year.Id, year.StartsIn, year.IsCurrent, yearTerms);
        session.EnsureYearsEditable();

        // Enrolment history is cross-aggregate — it lives outside the session, so the service checks it.
        YearDependentsRow dependents = await _repository.YearDependentsAsync(yearId, cancellationToken);
        if (dependents.Enrollments > 0)
        {
            throw new AppErrorException(
                "This session has enrolment records, so its dates are locked.", 409, ErrorCodes.Conflict);
        }

        if (request.StartYear <= 0 || request.EndYear <= 0)
        {
            throw new AppErrorException("Session start and end years are required.", 400, ErrorCodes.ValidationError);
        }

        if (request.EndYear != request.StartYear + 1)
        {
            throw new AppErrorException("Session duration must be exactly 1 year.", 400, ErrorCodes.ValidationError);
        }

        string name = BuildSessionName(request.StartYear, request.EndYear);
        int? startsIn = request.StartYear;

        if (await _repository.YearNameExistsAsync(name, yearId, cancellationToken))
        {
            throw new AppErrorException($"A session named \"{name}\" already exists.", 409, ErrorCodes.Conflict);
        }

        await _repository.UpdateYearAsync(yearId, name, startsIn, cancellationToken);
    }

    public async Task DeleteYearAsync(Guid yearId, CancellationToken cancellationToken)
    {
        if (!await _repository.YearExistsAsync(yearId, cancellationToken))
        {
            throw new AppErrorException("Academic year not found.", 404, ErrorCodes.NotFound);
        }

        // Deleting a session would cascade its terms (and their grades/fees) and orphan enrolment history —
        // block it while anything depends on it.
        YearDependentsRow dep = await _repository.YearDependentsAsync(yearId, cancellationToken);
        if (dep.HasAny)
        {
            List<string> parts = new List<string>();
            if (dep.Terms > 0) parts.Add($"{dep.Terms} term{(dep.Terms == 1 ? "" : "s")}");
            if (dep.Enrollments > 0) parts.Add($"{dep.Enrollments} enrolment record{(dep.Enrollments == 1 ? "" : "s")}");

            throw new AppErrorException(
                $"This session still has {string.Join(" and ", parts)}. Remove its terms first (a session with " +
                "enrolled students can't be deleted).",
                409, ErrorCodes.Conflict);
        }

        await _repository.DeleteYearAsync(yearId, cancellationToken);
    }

    public async Task<IReadOnlyList<TermResponse>> ListTermsAsync(Guid? academicYearId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TermRow> rows = await _repository.ListTermsAsync(academicYearId, cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<TermResponse> CreateTermAsync(CreateTermRequest request, CancellationToken cancellationToken)
    {
        if (request.Name is not Term term)
        {
            throw new AppErrorException("Term must be 'first', 'second', or 'third'.", 400, ErrorCodes.ValidationError);
        }

        AcademicYearRow year = await _repository.GetYearAsync(request.AcademicYearId, cancellationToken)
            ?? throw new AppErrorException("Academic year not found.", 404, ErrorCodes.NotFound);

        IReadOnlyList<TermRow> existing = await _repository.ListTermsAsync(request.AcademicYearId, cancellationToken);
        AcademicSession session = BuildSession(year.Id, year.StartsIn, year.IsCurrent, existing);

        // Ordering + one-of-each are invariants of the session aggregate.
        session.EnsureCanAddTerm(term);

        if (request.StartDate is DateOnly start && request.EndDate is DateOnly end)
        {
            session.EnsureTermDatesValid(term, start, end, excludeTermId: null);
            await EnsureNoSchoolWideOverlapAsync(start, end, excludeTermId: null, cancellationToken);
        }

        (Guid id, bool isCurrent) = await _repository.CreateTermAsync(request.AcademicYearId, term,
            request.StartDate, request.EndDate, cancellationToken);

        return new TermResponse
        {
            Id = id,
            AcademicYearId = request.AcademicYearId,
            Name = term,
            Season = AcademicSession.SeasonOf(term),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsCurrent = isCurrent
        };
    }

    public async Task SetCurrentTermAsync(Guid termId, CancellationToken cancellationToken)
    {
        TermRow term = await _repository.GetTermAsync(termId, cancellationToken)
            ?? throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);

        // The aggregate forbids setting the calendar backward past a term that has already started.
        IReadOnlyList<TermRow> siblings = await _repository.ListTermsAsync(term.AcademicYearId, cancellationToken);
        AcademicSession session = BuildSession(term.AcademicYearId, startYear: null, isCurrent: false, siblings);
        session.EnsureCanSetCurrentTerm(termId, TodayWat());

        await _repository.SetCurrentTermAsync(termId, cancellationToken);
    }

    // School days are West Africa Time (UTC+1, no DST).
    private static DateOnly TodayWat() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));

    public async Task UpdateTermDatesAsync(Guid termId, UpdateTermDatesRequest request, CancellationToken cancellationToken)
    {
        TermRow term = await _repository.GetTermAsync(termId, cancellationToken)
            ?? throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);

        if (request.StartDate is DateOnly start && request.EndDate is DateOnly end)
        {
            IReadOnlyList<TermRow> siblings = await _repository.ListTermsAsync(term.AcademicYearId, cancellationToken);
            AcademicYearRow? year = await _repository.GetYearAsync(term.AcademicYearId, cancellationToken);
            AcademicSession session = BuildSession(term.AcademicYearId, year?.StartsIn, year?.IsCurrent ?? false, siblings);

            session.EnsureTermDatesValid(SnakeCaseEnum.Parse<Term>(term.Name), start, end, excludeTermId: termId);
            await EnsureNoSchoolWideOverlapAsync(start, end, excludeTermId: termId, cancellationToken);
        }

        await _repository.UpdateTermDatesAsync(termId, request.StartDate, request.EndDate, cancellationToken);
    }

    public async Task DeleteTermAsync(Guid termId, CancellationToken cancellationToken)
    {
        TermRow term = await _repository.GetTermAsync(termId, cancellationToken)
            ?? throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);

        // Reverse-order removal is a structural invariant of the session aggregate.
        IReadOnlyList<TermRow> siblings = await _repository.ListTermsAsync(term.AcademicYearId, cancellationToken);
        AcademicSession session = BuildSession(term.AcademicYearId, startYear: null, isCurrent: false, siblings);
        session.EnsureCanRemoveTerm(termId);

        // Deleting a term would cascade its grades, report cards and fee types — block it while it holds data.
        TermDependentsRow dep = await _repository.TermDependentsAsync(termId, cancellationToken);
        if (dep.HasAny)
        {
            List<string> parts = new List<string>();
            if (dep.FeeTypes > 0) parts.Add($"{dep.FeeTypes} fee type{(dep.FeeTypes == 1 ? "" : "s")}");
            if (dep.Grades > 0) parts.Add("grade records");
            if (dep.ReportCards > 0) parts.Add("report cards");
            if (dep.Attendance > 0) parts.Add("attendance");
            if (dep.Payments > 0) parts.Add("payments");

            throw new AppErrorException(
                $"This term has {string.Join(", ", parts)} recorded against it and can't be deleted — that " +
                "history must be kept.",
                409, ErrorCodes.Conflict);
        }

        await _repository.DeleteTermAsync(termId, cancellationToken);
    }

    /// <summary>Rebuild the <see cref="AcademicSession"/> aggregate from persisted rows.</summary>
    private static AcademicSession BuildSession(Guid yearId, int? startYear, bool isCurrent,
        IReadOnlyList<TermRow> terms)
    {
        IEnumerable<SessionTerm> sessionTerms = (terms ?? Array.Empty<TermRow>())
            .Select(t => new SessionTerm(t.Id, SnakeCaseEnum.Parse<Term>(t.Name), t.StartDate, t.EndDate, t.IsCurrent));
        return new AcademicSession(yearId, startYear, isCurrent, sessionTerms);
    }

    /// <summary>
    /// Cross-aggregate rule: a school runs one term at a time, so a term's dates must not overlap any other
    /// dated term across the whole school (not just this session) — which only the database can see.
    /// </summary>
    private async Task EnsureNoSchoolWideOverlapAsync(DateOnly start, DateOnly end, Guid? excludeTermId,
        CancellationToken cancellationToken)
    {
        if (await _repository.HasDateOverlapAsync(start, end, excludeTermId, cancellationToken))
        {
            throw new AppErrorException(
                "These dates overlap another term. A school runs one session and one term at a time.",
                409, ErrorCodes.Conflict);
        }
    }

    private static string BuildSessionName(int startYear, int endYear)
    {
        return $"{startYear}/{endYear % 100:00}";
    }

    private static AcademicYearResponse MapAcademicYear(AcademicYearRow r)
    {
        string[] parts = r.Name.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        int startYear = parts.Length > 0 ? ParseYearValue(parts[0]) : 0;
        int endYear = parts.Length > 1 ? ParseYearValue(parts[1]) : 0;

        return new AcademicYearResponse
        {
            Id = r.Id,
            Name = r.Name,
            StartYear = startYear,
            EndYear = endYear,
            IsCurrent = r.IsCurrent
        };
    }

    private static int ParseYearValue(string value)
    {
        if (int.TryParse(value, out int parsed))
        {
            return value.Length <= 2 ? 2000 + parsed : parsed;
        }

        return 0;
    }

    private static TermResponse Map(TermRow r)
    {
        Term name = SnakeCaseEnum.Parse<Term>(r.Name);
        return new TermResponse
        {
            Id = r.Id,
            AcademicYearId = r.AcademicYearId,
            Name = name,
            Season = AcademicSession.SeasonOf(name),
            StartDate = r.StartDate,
            EndDate = r.EndDate,
            IsCurrent = r.IsCurrent
        };
    }
}
