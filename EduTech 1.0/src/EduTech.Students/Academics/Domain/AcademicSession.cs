using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Students.Academics.Domain;

/// <summary>A term as it lives inside a session (id + name + optional dates + current flag).</summary>
internal sealed class SessionTerm
{
    public SessionTerm(Guid id, Term name, DateOnly? startDate, DateOnly? endDate, bool isCurrent)
    {
        Id = id;
        Name = name;
        StartDate = startDate;
        EndDate = endDate;
        IsCurrent = isCurrent;
    }

    public Guid Id { get; }
    public Term Name { get; }
    public DateOnly? StartDate { get; }
    public DateOnly? EndDate { get; }
    public bool IsCurrent { get; }
}

/// <summary>
/// The academic calendar aggregate: a session (academic year) and the terms beneath it. It is the single
/// place the calendar's <b>structural invariants</b> live — term ordering, the session date window, the
/// edit/lifecycle lock, and reverse-order removal — so no service path can bypass them.
///
/// Cross-aggregate concerns stay in the service: school-wide "one term at a time" overlap (spans other
/// sessions) and "has dependent records" (grades/fees/enrolments) both need the database.
/// </summary>
internal sealed class AcademicSession
{
    private readonly List<SessionTerm> _terms;

    public AcademicSession(Guid id, int? startYear, bool isCurrent, IEnumerable<SessionTerm>? terms)
    {
        Id = id;
        StartYear = startYear;
        IsCurrent = isCurrent;
        _terms = (terms ?? Enumerable.Empty<SessionTerm>()).OrderBy(t => t.Name).ToList();
    }

    public Guid Id { get; }

    /// <summary>Calendar year the session starts (its <c>starts_in</c> anchor); null on legacy rows.</summary>
    public int? StartYear { get; }

    public bool IsCurrent { get; }
    public IReadOnlyList<SessionTerm> Terms => _terms;
    public bool HasTerms => _terms.Count > 0;

    // A Nigerian session runs Sept → Aug, so consecutive sessions don't overlap. The three terms are
    // named by season (First=Winter, Second=Spring, Third=Summer) for display — we deliberately do NOT
    // pin them to exact months, because schools resume at their own pace.

    /// <summary>The session boundary: 1 Sept (StartYear) → 31 Aug (StartYear+1). Null on legacy rows.</summary>
    public DateOnly? SessionStart => StartYear is int y ? new DateOnly(y, 9, 1) : null;
    public DateOnly? SessionEnd => StartYear is int y ? new DateOnly(y + 1, 8, 31) : null;

    /// <summary>The season a term is displayed as: First→Winter, Second→Spring, Third→Summer.</summary>
    public static string SeasonOf(Term term) => term switch
    {
        Term.First => "Winter",
        Term.Second => "Spring",
        Term.Third => "Summer",
        _ => "Term",
    };

    /// <summary>
    /// A session's years anchor every term, result, fee and enrolment beneath it. Once it is the ongoing
    /// (current) session, or already has terms, shifting its dates would misalign all of that.
    /// </summary>
    public void EnsureYearsEditable()
    {
        if (IsCurrent)
        {
            throw new AppErrorException(
                "The ongoing session can't be edited — its dates anchor live terms, results and fees.",
                409, ErrorCodes.Conflict);
        }

        if (HasTerms)
        {
            throw new AppErrorException(
                "This session already has terms, so its dates are locked. Remove its terms first to change it.",
                409, ErrorCodes.Conflict);
        }
    }

    /// <summary>
    /// A term can be added if it isn't already present and it is the next one in sequence. A school may
    /// START at any term (they can onboard mid-year), but after that no skipping and no going backward.
    /// </summary>
    public void EnsureCanAddTerm(Term name)
    {
        if (_terms.Any(t => t.Name == name))
        {
            throw new AppErrorException($"{name} term already exists for this session.", 409, ErrorCodes.Conflict);
        }

        if (_terms.Count == 0)
        {
            return; // first term of the session — any of the three is allowed.
        }

        Term highest = _terms.Max(t => t.Name);
        if (highest == Term.Third)
        {
            throw new AppErrorException("This session already has all three terms.", 409, ErrorCodes.Conflict);
        }

        Term next = highest + 1;
        if (name != next)
        {
            throw new AppErrorException($"Terms are added in order — add {next} term next.", 409, ErrorCodes.Conflict);
        }
    }

    /// <summary>
    /// Date rules for a term (on add or reschedule): end ≥ start, both dates inside the session window,
    /// starts after every earlier term ends, ends before every later term starts.
    /// <paramref name="excludeTermId"/> skips the term being rescheduled.
    /// </summary>
    public void EnsureTermDatesValid(Term self, DateOnly start, DateOnly end, Guid? excludeTermId)
    {
        if (end < start)
        {
            throw new AppErrorException("End date cannot be before the start date.", 400, ErrorCodes.ValidationError);
        }

        // The term must fall within its session's own Sept–Aug span. We deliberately don't pin terms to
        // exact months — schools resume at their own pace — only to the session boundary.
        if (SessionStart is DateOnly ss && SessionEnd is DateOnly se && (start < ss || end > se))
        {
            throw new AppErrorException(
                $"{SeasonOf(self)} term dates must fall within the {StartYear}/{StartYear + 1} session " +
                $"({ss:d MMM yyyy} – {se:d MMM yyyy}).",
                400, ErrorCodes.ValidationError);
        }

        List<SessionTerm> others = _terms.Where(t => t.Id != excludeTermId).ToList();

        DateOnly? priorEnd = others.Where(o => o.Name < self && o.EndDate is not null).Select(o => o.EndDate).Max();
        if (priorEnd is DateOnly pe && start <= pe)
        {
            throw new AppErrorException(
                $"This term must start after the previous term ends ({pe:yyyy-MM-dd}).", 409, ErrorCodes.Conflict);
        }

        DateOnly? nextStart = others.Where(o => o.Name > self && o.StartDate is not null).Select(o => o.StartDate).Min();
        if (nextStart is DateOnly ns && end >= ns)
        {
            throw new AppErrorException(
                $"This term must end before the next term starts ({ns:yyyy-MM-dd}).", 409, ErrorCodes.Conflict);
        }
    }

    /// <summary>
    /// A term can be made current unless a LATER term in the same session has already started — that later
    /// term is the real current one, so the calendar can't be set backward. Holiday gaps are fine (no later
    /// term has started yet), and setting an upcoming term current early (a school resuming ahead of the
    /// default dates) is allowed.
    /// </summary>
    public void EnsureCanSetCurrentTerm(Guid termId, DateOnly today)
    {
        SessionTerm term = _terms.FirstOrDefault(t => t.Id == termId)
            ?? throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);

        bool laterTermStarted = _terms.Any(t =>
            t.Name > term.Name && t.StartDate is DateOnly start && start <= today);
        if (laterTermStarted)
        {
            throw new AppErrorException(
                "A later term in this session has already started — that's the current term. " +
                "You can't set the calendar back to an earlier one.",
                409, ErrorCodes.Conflict);
        }
    }

    /// <summary>Terms are removed in reverse order so the sequence stays contiguous (no First + Third gaps).</summary>
    public void EnsureCanRemoveTerm(Guid termId)
    {
        SessionTerm term = _terms.FirstOrDefault(t => t.Id == termId)
            ?? throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);

        Term highest = _terms.Max(t => t.Name);
        if (term.Name != highest)
        {
            throw new AppErrorException(
                $"Delete {highest} term first — terms are removed in reverse order to stay in sequence.",
                409, ErrorCodes.Conflict);
        }
    }

}
