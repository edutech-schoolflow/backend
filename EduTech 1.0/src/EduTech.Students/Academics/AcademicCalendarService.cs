using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Students.Academics;

public interface IAcademicCalendarService
{
    Task<IReadOnlyList<AcademicYearResponse>> ListYearsAsync(CancellationToken cancellationToken);
    Task<AcademicYearResponse> CreateYearAsync(CreateAcademicYearRequest request, CancellationToken cancellationToken);
    Task SetCurrentYearAsync(Guid yearId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TermResponse>> ListTermsAsync(Guid? academicYearId, CancellationToken cancellationToken);
    Task<TermResponse> CreateTermAsync(CreateTermRequest request, CancellationToken cancellationToken);
    Task SetCurrentTermAsync(Guid termId, CancellationToken cancellationToken);
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
        if (name.Length == 0)
        {
            throw new AppErrorException("Academic year name is required.", 400, ErrorCodes.ValidationError);
        }

        // Session names are unique per school (the DB enforces it) — fail with a clear message rather than
        // letting the unique-constraint violation surface as a 500.
        if (await _repository.YearNameExistsAsync(name, cancellationToken))
        {
            throw new AppErrorException($"A session named \"{name}\" already exists.", 409, ErrorCodes.Conflict);
        }

        (Guid id, bool isCurrent) = await _repository.CreateYearAsync(name, cancellationToken);
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
            if (end < start)
            {
                throw new AppErrorException("End date cannot be before the start date.", 400, ErrorCodes.ValidationError);
            }

            // Must start after this session's previous term ends.
            DateOnly? priorEnd = existing.Where(t => t.EndDate is not null).Select(t => t.EndDate).Max();
            if (priorEnd is DateOnly pe && start <= pe)
            {
                throw new AppErrorException(
                    $"This term must start after the previous term ends ({pe:yyyy-MM-dd}).", 409, ErrorCodes.Conflict);
            }

            // A school runs one session/term at a time — reject any overlap with another dated term.
            if (await _repository.HasDateOverlapAsync(start, end, cancellationToken))
            {
                throw new AppErrorException(
                    "These dates overlap another term. A school runs one session and one term at a time.",
                    409, ErrorCodes.Conflict);
            }
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
