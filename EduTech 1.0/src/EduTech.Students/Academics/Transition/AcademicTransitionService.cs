using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Students.Academics.Domain;

namespace EduTech.Students.Academics.Transition;

/// <summary>
/// Term/session transitions for the CURRENT school (auto-prepare + confirm). The proposal is derived
/// from the school's own term dates against the clock; confirming moves the current-term (and, on a
/// session boundary, current-year) pointers — after the promotion gate, since enrollments are
/// per-session and a school must never be moved into a session its students aren't enrolled in.
/// </summary>
public interface IAcademicTransitionService
{
    /// <summary>The transition state as of <paramref name="asOf"/> (null → today, WAT).</summary>
    Task<TransitionProposalResponse> GetProposalAsync(DateOnly? asOf, CancellationToken cancellationToken);

    /// <summary>Apply the due transition (creating the next term/session if roll-forward hasn't yet).</summary>
    Task<TransitionProposalResponse> ConfirmAsync(DateOnly? asOf, CancellationToken cancellationToken);
}

internal sealed class AcademicTransitionService : IAcademicTransitionService
{
    private readonly IAcademicCalendarRepository _repository;
    private readonly IAcademicCalendarService _calendar;

    public AcademicTransitionService(IAcademicCalendarRepository repository, IAcademicCalendarService calendar)
    {
        _repository = repository;
        _calendar = calendar;
    }

    public async Task<TransitionProposalResponse> GetProposalAsync(DateOnly? asOf, CancellationToken cancellationToken)
    {
        return (await ComputeAsync(asOf, cancellationToken)).Response;
    }

    public async Task<TransitionProposalResponse> ConfirmAsync(DateOnly? asOf, CancellationToken cancellationToken)
    {
        (TransitionProposalResponse proposal, TransitionContext? context) = await ComputeAsync(asOf, cancellationToken);
        if (proposal.Status != TransitionStatus.TransitionDue || context is null)
        {
            throw new AppErrorException("No term transition is due right now.", 409, ErrorCodes.Conflict);
        }

        if (!context.IsSessionBoundary)
        {
            Guid termId = context.NextTermRow?.Id
                ?? await CreateTermSafeAsync(context.CurrentYear.Id, context.NextTerm, context.SessionStartYear, cancellationToken);
            await _repository.SetCurrentTermAsync(termId, cancellationToken);
            return await GetProposalAsync(asOf, cancellationToken);
        }

        // ── Session boundary ─────────────────────────────────────────────────
        if (context.SessionStartYear is not int startYear)
        {
            throw new AppErrorException(
                "This session's start year is unknown, so the next session can't be derived. Set it on the calendar first.",
                409, ErrorCodes.Conflict);
        }

        Guid nextYearId = context.NextYearRow?.Id
            ?? (await _calendar.CreateYearAsync(
                new CreateAcademicYearRequest { StartYear = startYear + 1, EndYear = startYear + 2 }, cancellationToken)).Id;

        int awaiting = await _repository.CountActiveStudentsNotInYearAsync(nextYearId, cancellationToken);
        if (awaiting > 0)
        {
            throw new AppErrorException(
                $"{awaiting} active student(s) haven't been promoted into the new session yet. Run end-of-session " +
                "promotion first, then confirm the new session.",
                409, ErrorCodes.Conflict);
        }

        Guid firstTermId = context.NextTermRow?.Id
            ?? await CreateTermSafeAsync(nextYearId, Term.First, startYear + 1, cancellationToken);

        await _repository.SetCurrentYearAsync(nextYearId, cancellationToken);
        await _repository.SetCurrentTermAsync(firstTermId, cancellationToken);
        return await GetProposalAsync(asOf, cancellationToken);
    }

    // ── proposal computation ──────────────────────────────────────────────────

    private sealed class TransitionContext
    {
        public required TermRow CurrentTermRow { get; init; }
        public required AcademicYearRow CurrentYear { get; init; }
        public required Term NextTerm { get; init; }
        public required bool IsSessionBoundary { get; init; }
        public int? SessionStartYear { get; init; }
        public TermRow? NextTermRow { get; init; }
        public AcademicYearRow? NextYearRow { get; init; }
    }

    private async Task<(TransitionProposalResponse Response, TransitionContext? Context)> ComputeAsync(
        DateOnly? asOf, CancellationToken cancellationToken)
    {
        DateOnly today = asOf ?? TodayWat();

        IReadOnlyList<TermRow> terms = await _repository.ListTermsAsync(null, cancellationToken);
        TermRow? current = terms.FirstOrDefault(t => t.IsCurrent);
        if (current is null)
        {
            return (new TransitionProposalResponse { Status = TransitionStatus.NoCurrentTerm }, null);
        }

        AcademicYearRow year = await _repository.GetYearAsync(current.AcademicYearId, cancellationToken)
            ?? throw new AppErrorException("The current term's session no longer exists.", 409, ErrorCodes.Conflict);

        Term currentTerm = SnakeCaseEnum.Parse<Term>(current.Name);

        // No end date = the school manages its calendar by hand; the clock proposes nothing.
        if (current.EndDate is not DateOnly endDate || endDate >= today)
        {
            return (new TransitionProposalResponse
            {
                Status = TransitionStatus.TermOngoing,
                CurrentTerm = currentTerm,
                CurrentSession = year.Name,
                CurrentTermEndDate = current.EndDate
            }, null);
        }

        int? startYear = StartYearOf(year);
        bool boundary = currentTerm == Term.Third;
        Term nextTerm = boundary ? Term.First : currentTerm + 1;

        TermRow? nextRow;
        AcademicYearRow? nextYearRow = null;
        int? awaiting = null;

        if (!boundary)
        {
            nextRow = terms.FirstOrDefault(t =>
                t.AcademicYearId == current.AcademicYearId && SnakeCaseEnum.Parse<Term>(t.Name) == nextTerm);
        }
        else
        {
            IReadOnlyList<AcademicYearRow> years = await _repository.ListYearsAsync(cancellationToken);
            nextYearRow = startYear is int sy ? years.FirstOrDefault(y => StartYearOf(y) == sy + 1) : null;
            nextRow = nextYearRow is null
                ? null
                : terms.FirstOrDefault(t =>
                    t.AcademicYearId == nextYearRow.Id && SnakeCaseEnum.Parse<Term>(t.Name) == Term.First);
            if (nextYearRow is not null)
            {
                awaiting = await _repository.CountActiveStudentsNotInYearAsync(nextYearRow.Id, cancellationToken);
            }
        }

        TransitionContext context = new TransitionContext
        {
            CurrentTermRow = current,
            CurrentYear = year,
            NextTerm = nextTerm,
            IsSessionBoundary = boundary,
            SessionStartYear = startYear,
            NextTermRow = nextRow,
            NextYearRow = nextYearRow
        };

        return (new TransitionProposalResponse
        {
            Status = TransitionStatus.TransitionDue,
            CurrentTerm = currentTerm,
            CurrentSession = year.Name,
            CurrentTermEndDate = endDate,
            NextTerm = nextTerm,
            NextSessionStartYear = boundary ? startYear + 1 : startYear,
            IsSessionBoundary = boundary,
            NextTermPrepared = nextRow is not null,
            NextTermId = nextRow?.Id,
            StudentsAwaitingPromotion = awaiting
        }, context);
    }

    /// <summary>
    /// Creates the next term with the policy's default dates; a school whose custom dates collide
    /// with the defaults gets a dateless term instead (they date it themselves) — the transition
    /// must never be blocked by our own defaults.
    /// </summary>
    private async Task<Guid> CreateTermSafeAsync(Guid yearId, Term term, int? sessionStartYear,
        CancellationToken cancellationToken)
    {
        DateOnly? start = null, end = null;
        if (sessionStartYear is int sy)
        {
            (start, end) = NigerianCalendarPolicy.DefaultDatesFor(sy, term);
        }

        try
        {
            return (await _calendar.CreateTermAsync(
                new CreateTermRequest { AcademicYearId = yearId, Name = term, StartDate = start, EndDate = end },
                cancellationToken)).Id;
        }
        catch (AppErrorException) when (start is not null)
        {
            return (await _calendar.CreateTermAsync(
                new CreateTermRequest { AcademicYearId = yearId, Name = term }, cancellationToken)).Id;
        }
    }

    /// <summary>starts_in, falling back to parsing the "2025/26" session name on legacy rows.</summary>
    private static int? StartYearOf(AcademicYearRow year)
    {
        if (year.StartsIn is int anchored)
        {
            return anchored;
        }

        string head = year.Name.Split('/')[0];
        return int.TryParse(head, out int parsed) ? parsed : null;
    }

    // School days are West Africa Time (UTC+1, no DST).
    private static DateOnly TodayWat() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
}
