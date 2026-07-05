using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Students.Academics.Domain;

namespace EduTech.Auth.Tests.Students;

/// <summary>
/// Pure domain tests for the calendar aggregate — no database, no mocks. This is the payoff of moving the
/// invariants into <see cref="AcademicSession"/>: every rule is exercised in isolation, fast.
/// </summary>
public class AcademicSessionTests
{
    // A term with no dates — enough for the ordering / removal / edit-lock rules.
    private static SessionTerm Term_(Term name) =>
        new SessionTerm(Guid.NewGuid(), name, null, null, false);

    private static AcademicSession Session(int? startYear, bool isCurrent, params SessionTerm[] terms) =>
        new AcademicSession(Guid.NewGuid(), startYear, isCurrent, terms);

    // ── edit lock ────────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureYearsEditable_FutureEmptySession_Ok()
    {
        Session(2030, isCurrent: false).EnsureYearsEditable(); // no throw
    }

    [Fact]
    public void EnsureYearsEditable_OngoingSession_Throws409()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(
            () => Session(2025, isCurrent: true).EnsureYearsEditable());
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void EnsureYearsEditable_SessionWithTerms_Throws409()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(
            () => Session(2025, isCurrent: false, Term_(Term.First)).EnsureYearsEditable());
        Assert.Equal(409, ex.StatusCode);
    }

    // ── term ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureCanAddTerm_EmptySession_AllowsAnyStartTerm()
    {
        Session(2025, false).EnsureCanAddTerm(Term.Second); // onboard mid-year — no throw
    }

    [Fact]
    public void EnsureCanAddTerm_Duplicate_Throws409()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(
            () => Session(2025, false, Term_(Term.First)).EnsureCanAddTerm(Term.First));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void EnsureCanAddTerm_SkippingAhead_Throws409()
    {
        // First exists → next must be Second, not Third.
        AppErrorException ex = Assert.Throws<AppErrorException>(
            () => Session(2025, false, Term_(Term.First)).EnsureCanAddTerm(Term.Third));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void EnsureCanAddTerm_NextInSequence_Ok()
    {
        Session(2025, false, Term_(Term.First)).EnsureCanAddTerm(Term.Second); // no throw
    }

    // ── term dates ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureTermDatesValid_EndBeforeStart_Throws400()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() => Session(2025, false)
            .EnsureTermDatesValid(Term.First, new DateOnly(2025, 9, 10), new DateOnly(2025, 9, 1), null));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void EnsureTermDatesValid_OutsideSessionWindow_Throws400()
    {
        // 2025/2026 session must reject a 2027 end date (the reported bug).
        AppErrorException ex = Assert.Throws<AppErrorException>(() => Session(2025, false)
            .EnsureTermDatesValid(Term.First, new DateOnly(2025, 12, 11), new DateOnly(2027, 11, 11), null));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void EnsureTermDatesValid_StartsBeforePriorTermEnds_Throws409()
    {
        SessionTerm first = new SessionTerm(Guid.NewGuid(), Term.First,
            new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 20), false);
        AppErrorException ex = Assert.Throws<AppErrorException>(() => Session(2025, false, first)
            .EnsureTermDatesValid(Term.Second, new DateOnly(2025, 12, 10), new DateOnly(2026, 3, 1), null));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void EnsureTermDatesValid_WithinWindowAfterPrior_Ok()
    {
        SessionTerm first = new SessionTerm(Guid.NewGuid(), Term.First,
            new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 20), false);
        Session(2025, false, first)
            .EnsureTermDatesValid(Term.Second, new DateOnly(2026, 1, 10), new DateOnly(2026, 4, 1), null); // no throw
    }

    [Fact]
    public void EnsureTermDatesValid_DateBeforeSessionStarts_Throws400()
    {
        // The reported bug: a Third term of the 2027/2028 session dated in July 2027 — before the
        // session even begins (1 Sept 2027). Months aren't enforced, but the session boundary is.
        AppErrorException ex = Assert.Throws<AppErrorException>(() => Session(2027, false)
            .EnsureTermDatesValid(Term.Third, new DateOnly(2027, 7, 5), new DateOnly(2027, 7, 6), null));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void EnsureTermDatesValid_AnyMonthWithinSession_Ok()
    {
        // No month pinning: a First term running Oct–Feb is fine as long as it's inside the session.
        Session(2027, false)
            .EnsureTermDatesValid(Term.First, new DateOnly(2027, 10, 1), new DateOnly(2028, 2, 1), null); // no throw
    }

    [Fact]
    public void SeasonOf_MapsTermsToSeasons()
    {
        Assert.Equal("Winter", AcademicSession.SeasonOf(Term.First));
        Assert.Equal("Spring", AcademicSession.SeasonOf(Term.Second));
        Assert.Equal("Summer", AcademicSession.SeasonOf(Term.Third));
    }

    // ── term removal ───────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureCanRemoveTerm_NotHighest_Throws409()
    {
        SessionTerm first = Term_(Term.First);
        SessionTerm second = Term_(Term.Second);
        AppErrorException ex = Assert.Throws<AppErrorException>(
            () => Session(2025, false, first, second).EnsureCanRemoveTerm(first.Id));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void EnsureCanRemoveTerm_Highest_Ok()
    {
        SessionTerm first = Term_(Term.First);
        SessionTerm second = Term_(Term.Second);
        Session(2025, false, first, second).EnsureCanRemoveTerm(second.Id); // no throw
    }
}
