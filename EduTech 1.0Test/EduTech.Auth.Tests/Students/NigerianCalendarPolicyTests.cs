using EduTech.Shared.Constants;
using EduTech.Students.Academics.Domain;

namespace EduTech.Auth.Tests.Students;

public class NigerianCalendarPolicyTests
{
    // ---- which session a date belongs to (session runs 1 Sept → 31 Aug) ----

    [Theory]
    [InlineData(2025, 9, 1, 2025)]     // first day of the session
    [InlineData(2025, 12, 15, 2025)]
    [InlineData(2026, 1, 10, 2025)]    // January still belongs to the 2025/26 session
    [InlineData(2026, 8, 31, 2025)]    // last day of the session
    [InlineData(2026, 9, 1, 2026)]     // rollover
    public void SessionStartYearFor_MapsDateToSession(int y, int m, int d, int expectedStartYear)
    {
        Assert.Equal(expectedStartYear, NigerianCalendarPolicy.SessionStartYearFor(new DateOnly(y, m, d)));
    }

    // ---- default term windows must satisfy the AcademicSession invariants ----

    [Fact]
    public void DefaultDates_AreOrderedAndInsideTheSessionWindow()
    {
        int startYear = 2025;
        (DateOnly firstStart, DateOnly firstEnd) = NigerianCalendarPolicy.DefaultDatesFor(startYear, Term.First);
        (DateOnly secondStart, DateOnly secondEnd) = NigerianCalendarPolicy.DefaultDatesFor(startYear, Term.Second);
        (DateOnly thirdStart, DateOnly thirdEnd) = NigerianCalendarPolicy.DefaultDatesFor(startYear, Term.Third);

        // Inside 1 Sept 2025 – 31 Aug 2026.
        Assert.True(firstStart >= new DateOnly(2025, 9, 1));
        Assert.True(thirdEnd <= new DateOnly(2026, 8, 31));

        // Strictly ordered with holiday gaps between terms.
        Assert.True(firstStart < firstEnd);
        Assert.True(firstEnd < secondStart);
        Assert.True(secondStart < secondEnd);
        Assert.True(secondEnd < thirdStart);
        Assert.True(thirdStart < thirdEnd);
    }

    // ---- which term should be "current" for a date (containment, else most recently started) ----

    [Theory]
    [InlineData(2025, 10, 15, Term.First)]     // inside first term
    [InlineData(2025, 12, 30, Term.First)]     // Christmas holiday → the term just ended
    [InlineData(2026, 2, 10, Term.Second)]     // inside second term
    [InlineData(2026, 4, 20, Term.Second)]     // Easter gap → second term still the reference
    [InlineData(2026, 6, 10, Term.Third)]      // inside third term
    [InlineData(2026, 8, 20, Term.Third)]      // long vacation → third term
    [InlineData(2025, 9, 5, Term.First)]       // session started but first term not yet resumed
    public void CurrentTermFor_ResolvesTermForDate(int y, int m, int d, Term expected)
    {
        (int startYear, Term term) = NigerianCalendarPolicy.CurrentTermFor(new DateOnly(y, m, d));
        Assert.Equal(expected, term);
        Assert.Equal(2025, startYear);
    }

    // ---- roll-forward: what comes after a term ----

    [Fact]
    public void NextAfter_AdvancesTermsWithinSession()
    {
        Assert.Equal((2025, Term.Second), NigerianCalendarPolicy.NextAfter(2025, Term.First));
        Assert.Equal((2025, Term.Third), NigerianCalendarPolicy.NextAfter(2025, Term.Second));
    }

    [Fact]
    public void NextAfter_ThirdTerm_RollsIntoNextSession()
    {
        Assert.Equal((2026, Term.First), NigerianCalendarPolicy.NextAfter(2025, Term.Third));
    }
}
