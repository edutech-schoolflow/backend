using EduTech.Shared.Constants;
using EduTech.Shared.Notifications;
using EduTech.Students.Academics.Domain;
using Microsoft.Extensions.Logging;

namespace EduTech.Students.Academics.Transition;

/// <summary>
/// The daily calendar sweep (Hangfire recurring job). For every school it:
///   • provisions a first calendar (current session + three default-dated terms) if none exists;
///   • when the current term's end date has passed, PREPARES the next term/session (never flips the
///     current pointers — that's the school's confirm) and SMSes the owner once, on creation.
/// Idempotent by construction: an already-prepared next term means a past run handled (and notified
/// about) this transition, so the sweep stays silent. Schools with a dateless current term manage
/// their calendar by hand and are never touched.
/// </summary>
public sealed class CalendarRollForwardJob
{
    private readonly ICalendarRollForwardRepository _repository;
    private readonly INotificationDispatcher _notifications;
    private readonly ILogger<CalendarRollForwardJob> _logger;

    public CalendarRollForwardJob(ICalendarRollForwardRepository repository,
        INotificationDispatcher notifications, ILogger<CalendarRollForwardJob> logger)
    {
        _repository = repository;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>Hangfire entry point — runs against today's date (WAT).</summary>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        return RunAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)), cancellationToken);
    }

    public async Task RunAsync(DateOnly today, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> schools = await _repository.ListSchoolIdsAsync(cancellationToken);
        foreach (Guid schoolId in schools)
        {
            try
            {
                await SweepSchoolAsync(schoolId, today, cancellationToken);
            }
            catch (Exception ex)
            {
                // One school's bad calendar must not stop the sweep for everyone else.
                _logger.LogError(ex, "Calendar roll-forward failed for school {SchoolId}.", schoolId);
            }
        }
    }

    private async Task SweepSchoolAsync(Guid schoolId, DateOnly today, CancellationToken cancellationToken)
    {
        CalendarSnapshotRow snapshot = await _repository.GetSnapshotAsync(schoolId, cancellationToken);

        if (!snapshot.HasYears)
        {
            (int startYear, Term currentTerm) = NigerianCalendarPolicy.CurrentTermFor(today);
            List<(Term, DateOnly, DateOnly)> windows = new List<(Term, DateOnly, DateOnly)>(3);
            foreach (Term term in new[] { Term.First, Term.Second, Term.Third })
            {
                (DateOnly start, DateOnly end) = NigerianCalendarPolicy.DefaultDatesFor(startYear, term);
                windows.Add((term, start, end));
            }

            await _repository.ProvisionCalendarAsync(schoolId, startYear, currentTerm, windows, cancellationToken);
            _logger.LogInformation("Provisioned {Session} calendar for school {SchoolId}.",
                CalendarRollForwardRepository.SessionName(startYear), schoolId);
            return;
        }

        // Dateless current term (or none marked) → manually managed → never auto-advanced.
        if (snapshot.CurrentYearId is not Guid yearId
            || snapshot.CurrentTermName is null
            || snapshot.CurrentTermEndDate is not DateOnly endDate
            || endDate >= today)
        {
            return;
        }

        int? startYearMaybe = snapshot.CurrentYearStartsIn ?? ParseStartYear(snapshot.CurrentYearName);
        if (startYearMaybe is not int sessionStartYear)
        {
            _logger.LogWarning("School {SchoolId}: current session '{Name}' has no derivable start year; skipping.",
                schoolId, snapshot.CurrentYearName);
            return;
        }

        Term currentTermName = SnakeCaseEnum.Parse<Term>(snapshot.CurrentTermName);
        (int nextStartYear, Term nextTerm) = NigerianCalendarPolicy.NextAfter(sessionStartYear, currentTermName);

        Guid targetYearId = yearId;
        if (nextTerm == Term.First)
        {
            targetYearId = await _repository.EnsureYearAsync(schoolId,
                CalendarRollForwardRepository.SessionName(nextStartYear), nextStartYear, cancellationToken);
        }

        if (await _repository.TermExistsInYearAsync(schoolId, targetYearId, nextTerm, cancellationToken))
        {
            return;   // already prepared (and the owner already notified) by an earlier run
        }

        await _repository.PrepareTermInYearAsync(schoolId, targetYearId, nextTerm,
            NigerianCalendarPolicy.DefaultDatesFor(nextStartYear, nextTerm), cancellationToken);

        string? ownerPhone = await _repository.GetOwnerPhoneAsync(schoolId, cancellationToken);
        if (ownerPhone is not null)
        {
            string current = TermLabel(currentTermName);
            string next = TermLabel(nextTerm);
            string suffix = nextTerm == Term.First
                ? $" for the {CalendarRollForwardRepository.SessionName(nextStartYear)} session. Run promotion, then confirm the new session on your dashboard."
                : ". Review its dates and confirm the move on your dashboard.";
            await _notifications.SendSmsAsync(ownerPhone,
                $"SchoolFlow: {current} has ended. We've prepared {next}{suffix}", cancellationToken);
        }
    }

    private static string TermLabel(Term term) => term switch
    {
        Term.First => "First Term",
        Term.Second => "Second Term",
        _ => "Third Term"
    };

    private static int? ParseStartYear(string? sessionName)
    {
        string? head = sessionName?.Split('/')[0];
        return int.TryParse(head, out int parsed) ? parsed : null;
    }
}
