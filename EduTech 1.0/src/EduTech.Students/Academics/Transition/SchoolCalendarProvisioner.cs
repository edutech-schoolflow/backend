using EduTech.Shared.Constants;
using EduTech.Students.Academics.Domain;

namespace EduTech.Students.Academics.Transition;

/// <summary>
/// Provisions a school's first academic calendar from <see cref="NigerianCalendarPolicy"/> — the current
/// session with ONLY the term we're in (the season we're in), marked current. Past terms of the session
/// are not created: a school onboarding mid-year never ran them, and the roll-forward adds each following
/// term as it comes. Idempotent: a school that already has any session is left untouched, so it's safe to
/// call from both the activation event and the daily sweep.
/// </summary>
public interface ISchoolCalendarProvisioner
{
    /// <summary>Provisions the calendar if the school has none. Returns true if it created one.</summary>
    Task<bool> ProvisionIfMissingAsync(Guid schoolId, DateOnly asOf, CancellationToken cancellationToken);
}

internal sealed class SchoolCalendarProvisioner : ISchoolCalendarProvisioner
{
    private readonly ICalendarRollForwardRepository _repository;

    public SchoolCalendarProvisioner(ICalendarRollForwardRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> ProvisionIfMissingAsync(Guid schoolId, DateOnly asOf, CancellationToken cancellationToken)
    {
        CalendarSnapshotRow snapshot = await _repository.GetSnapshotAsync(schoolId, cancellationToken);
        if (snapshot.HasYears)
        {
            return false;
        }

        // Only the term we're currently in — no already-ended terms.
        (int startYear, Term currentTerm) = NigerianCalendarPolicy.CurrentTermFor(asOf);
        (DateOnly start, DateOnly end) = NigerianCalendarPolicy.DefaultDatesFor(startYear, currentTerm);
        List<(Term, DateOnly, DateOnly)> windows = new() { (currentTerm, start, end) };

        await _repository.ProvisionCalendarAsync(schoolId, startYear, currentTerm, windows, cancellationToken);
        return true;
    }
}
