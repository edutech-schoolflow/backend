using System.Text.RegularExpressions;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

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
        return rows.Select(r => new AcademicYearResponse { Id = r.Id, Name = r.Name, IsCurrent = r.IsCurrent }).ToList();
    }

    public async Task<AcademicYearResponse> CreateYearAsync(CreateAcademicYearRequest request,
        CancellationToken cancellationToken)
    {
        string name = (request.Name ?? string.Empty).Trim();
        int? startsIn = RequireStartYear(name);

        if (await _repository.YearNameExistsAsync(name, null, cancellationToken))
        {
            throw new AppErrorException($"A session named \"{name}\" already exists.", 409, ErrorCodes.Conflict);
        }

        (Guid id, bool isCurrent) = await _repository.CreateYearAsync(name, startsIn, cancellationToken);
        return new AcademicYearResponse { Id = id, Name = name, IsCurrent = isCurrent };
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
        if (!await _repository.YearExistsAsync(yearId, cancellationToken))
        {
            throw new AppErrorException("Academic year not found.", 404, ErrorCodes.NotFound);
        }

        string name = (request.Name ?? string.Empty).Trim();
        int? startsIn = RequireStartYear(name);

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

        if (!await _repository.YearExistsAsync(request.AcademicYearId, cancellationToken))
        {
            throw new AppErrorException("Academic year not found.", 404, ErrorCodes.NotFound);
        }

        // One of each term per session (DB-enforced) — friendly 409 instead of a 500.
        if (await _repository.TermNameExistsAsync(request.AcademicYearId, term, cancellationToken))
        {
            throw new AppErrorException($"{term} term already exists for this session.", 409, ErrorCodes.Conflict);
        }

        IReadOnlyList<TermRow> existing = await _repository.ListTermsAsync(request.AcademicYearId, cancellationToken);

        // Terms run in order (First → Second → Third). A school may START at any term (they can onboard
        // mid-year), but after that each new term must be the next one — no skipping, no going backward.
        if (existing.Count > 0)
        {
            Term highest = existing.Select(t => SnakeCaseEnum.Parse<Term>(t.Name)).Max();
            if (highest == Term.Third)
            {
                throw new AppErrorException("This session already has all three terms.", 409, ErrorCodes.Conflict);
            }

            Term next = highest + 1;
            if (term != next)
            {
                throw new AppErrorException($"Terms are added in order — add {next} term next.", 409, ErrorCodes.Conflict);
            }
        }

        if (request.StartDate is DateOnly start && request.EndDate is DateOnly end)
        {
            await ValidateTermDatesAsync(start, end, existing, term, excludeTermId: null, cancellationToken);
        }

        (Guid id, bool isCurrent) = await _repository.CreateTermAsync(request.AcademicYearId, term,
            request.StartDate, request.EndDate, cancellationToken);

        return new TermResponse
        {
            Id = id,
            AcademicYearId = request.AcademicYearId,
            Name = term,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsCurrent = isCurrent
        };
    }

    public async Task SetCurrentTermAsync(Guid termId, CancellationToken cancellationToken)
    {
        if (!await _repository.TermExistsAsync(termId, cancellationToken))
        {
            throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);
        }

        await _repository.SetCurrentTermAsync(termId, cancellationToken);
    }

    public async Task UpdateTermDatesAsync(Guid termId, UpdateTermDatesRequest request, CancellationToken cancellationToken)
    {
        TermRow term = await _repository.GetTermAsync(termId, cancellationToken)
            ?? throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);

        if (request.StartDate is DateOnly start && request.EndDate is DateOnly end)
        {
            IReadOnlyList<TermRow> siblings = await _repository.ListTermsAsync(term.AcademicYearId, cancellationToken);
            Term self = SnakeCaseEnum.Parse<Term>(term.Name);
            await ValidateTermDatesAsync(start, end, siblings, self, excludeTermId: termId, cancellationToken);
        }

        await _repository.UpdateTermDatesAsync(termId, request.StartDate, request.EndDate, cancellationToken);
    }

    public async Task DeleteTermAsync(Guid termId, CancellationToken cancellationToken)
    {
        TermRow term = await _repository.GetTermAsync(termId, cancellationToken)
            ?? throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);

        // Terms are removed in reverse order so the sequence stays contiguous (no First + Third gaps).
        IReadOnlyList<TermRow> siblings = await _repository.ListTermsAsync(term.AcademicYearId, cancellationToken);
        Term self = SnakeCaseEnum.Parse<Term>(term.Name);
        Term highest = siblings.Select(t => SnakeCaseEnum.Parse<Term>(t.Name)).Max();
        if (self != highest)
        {
            throw new AppErrorException(
                $"Delete {highest} term first — terms are removed in reverse order to stay in sequence.",
                409, ErrorCodes.Conflict);
        }

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

    /// <summary>Shared date rules: end ≥ start, starts after the prior term, ends before the next, no overlap.</summary>
    private async Task ValidateTermDatesAsync(DateOnly start, DateOnly end, IReadOnlyList<TermRow> sessionTerms,
        Term self, Guid? excludeTermId, CancellationToken cancellationToken)
    {
        if (end < start)
        {
            throw new AppErrorException("End date cannot be before the start date.", 400, ErrorCodes.ValidationError);
        }

        List<(Term Ord, TermRow Row)> others = sessionTerms
            .Where(t => t.Id != excludeTermId)
            .Select(t => (SnakeCaseEnum.Parse<Term>(t.Name), t))
            .ToList();

        // Must start after every earlier term in this session ends.
        DateOnly? priorEnd = others.Where(o => o.Ord < self && o.Row.EndDate is not null).Select(o => o.Row.EndDate).Max();
        if (priorEnd is DateOnly pe && start <= pe)
        {
            throw new AppErrorException(
                $"This term must start after the previous term ends ({pe:yyyy-MM-dd}).", 409, ErrorCodes.Conflict);
        }

        // Must end before every later term in this session starts.
        DateOnly? nextStart = others.Where(o => o.Ord > self && o.Row.StartDate is not null).Select(o => o.Row.StartDate).Min();
        if (nextStart is DateOnly ns && end >= ns)
        {
            throw new AppErrorException(
                $"This term must end before the next term starts ({ns:yyyy-MM-dd}).", 409, ErrorCodes.Conflict);
        }

        // A school runs one session/term at a time — reject any overlap with another dated term.
        if (await _repository.HasDateOverlapAsync(start, end, excludeTermId, cancellationToken))
        {
            throw new AppErrorException(
                "These dates overlap another term. A school runs one session and one term at a time.",
                409, ErrorCodes.Conflict);
        }
    }

    // Sessions are ordered by the calendar year they start (e.g. 2024 for "2024/2025"), so promotion and
    // progression have a real chronology — require a 4-digit year in the name.
    private static int? RequireStartYear(string name)
    {
        if (name.Length == 0)
        {
            throw new AppErrorException("Session name is required.", 400, ErrorCodes.ValidationError);
        }

        Match m = Regex.Match(name, @"\d{4}");
        if (!m.Success)
        {
            throw new AppErrorException(
                "Use a year-based session name like \"2024/2025\" so sessions stay in order.",
                400, ErrorCodes.ValidationError);
        }

        return int.Parse(m.Value);
    }

    private static TermResponse Map(TermRow r) => new TermResponse
    {
        Id = r.Id,
        AcademicYearId = r.AcademicYearId,
        Name = SnakeCaseEnum.Parse<Term>(r.Name),
        StartDate = r.StartDate,
        EndDate = r.EndDate,
        IsCurrent = r.IsCurrent
    };
}
