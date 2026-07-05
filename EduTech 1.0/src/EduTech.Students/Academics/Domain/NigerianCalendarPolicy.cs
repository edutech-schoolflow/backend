using EduTech.Shared.Constants;

namespace EduTech.Students.Academics.Domain;

/// <summary>
/// The platform's model of the standard Nigerian academic calendar: one source of truth for "which
/// session/term does a date fall in", the default term windows used to auto-provision a school's
/// calendar, and "what comes next" for roll-forward. Pure and clock-free — callers pass the date.
///
/// These are DEFAULTS, not law: the platform generates the calendar from this policy and schools
/// adjust their actual term dates through <see cref="AcademicSession"/> (schools resume at their own
/// pace — strikes, early/late resumption). Transitions of the CURRENT term/session are proposed by
/// the policy but only applied when the school confirms (auto-prepare + confirm, never silent flips).
/// The window constants below are the platform-wide baseline; when they need to vary per year, move
/// them into platform settings and feed them in here.
/// </summary>
internal static class NigerianCalendarPolicy
{
    // Default term windows, expressed month/day relative to the session's start year (September).
    // First:  mid-Sept → mid-Dec (of startYear); Second: early Jan → early Apr; Third: late Apr →
    // late Jul (of startYear + 1). Gaps between terms are the holidays.
    private static readonly (Term Term, int StartMonth, int StartDay, int EndMonth, int EndDay)[] Windows =
    {
        (Term.First,  9, 15, 12, 18),
        (Term.Second, 1,  5,  4, 10),
        (Term.Third,  4, 27,  7, 31),
    };

    /// <summary>The start year of the session containing <paramref name="date"/> (Sept–Aug window).</summary>
    public static int SessionStartYearFor(DateOnly date) =>
        date.Month >= 9 ? date.Year : date.Year - 1;

    /// <summary>The default window for a term of the <paramref name="sessionStartYear"/> session.</summary>
    public static (DateOnly Start, DateOnly End) DefaultDatesFor(int sessionStartYear, Term term)
    {
        (_, int sm, int sd, int em, int ed) = Windows.First(w => w.Term == term);
        int startYear = sm >= 9 ? sessionStartYear : sessionStartYear + 1;
        int endYear = em >= 9 ? sessionStartYear : sessionStartYear + 1;
        return (new DateOnly(startYear, sm, sd), new DateOnly(endYear, em, ed));
    }

    /// <summary>
    /// The term a school should treat as current on <paramref name="date"/>: the term whose default
    /// window contains it, or — during a holiday gap — the most recently started term (the one whose
    /// results/fees are still the live context). Before first-term resumption it is First.
    /// </summary>
    public static (int SessionStartYear, Term Term) CurrentTermFor(DateOnly date)
    {
        int startYear = SessionStartYearFor(date);

        Term current = Term.First;
        foreach (Term term in new[] { Term.First, Term.Second, Term.Third })
        {
            (DateOnly start, _) = DefaultDatesFor(startYear, term);
            if (date >= start)
            {
                current = term;
            }
        }

        return (startYear, current);
    }

    /// <summary>The term after this one — Third rolls into the next session's First.</summary>
    public static (int SessionStartYear, Term Term) NextAfter(int sessionStartYear, Term term) =>
        term == Term.Third ? (sessionStartYear + 1, Term.First) : (sessionStartYear, term + 1);
}
