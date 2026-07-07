using EduTech.Shared.Constants;
using EduTech.Shared.Notifications;
using EduTech.Students.Academics.Transition;
using EduTech.Students.Classes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EduTech.Auth.Tests.Students;

public class CalendarRollForwardJobTests
{
    private readonly Mock<ICalendarRollForwardRepository> _repo = new();
    private readonly Mock<ISchoolClassProvisioner> _classProvisioner = new();
    private readonly Mock<INotificationDispatcher> _notifications = new();

    // Calendar provisioning is delegated to the real provisioner over the mocked repo, so
    // ProvisionCalendarAsync is still observable on _repo; class provisioning is a no-op mock here.
    private CalendarRollForwardJob CreateSut() =>
        new(_repo.Object, new SchoolCalendarProvisioner(_repo.Object), _classProvisioner.Object,
            _notifications.Object, NullLogger<CalendarRollForwardJob>.Instance);

    private static readonly Guid School = Guid.NewGuid();
    private static readonly Guid YearId = Guid.NewGuid();

    private void Schools(params Guid[] ids) =>
        _repo.Setup(r => r.ListSchoolIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ids);

    [Fact]
    public async Task Run_SchoolWithNoCalendar_ProvisionsCurrentSession()
    {
        Schools(School);
        _repo.Setup(r => r.GetSnapshotAsync(School, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarSnapshotRow { HasYears = false });

        await CreateSut().RunAsync(new DateOnly(2025, 10, 10), CancellationToken.None);

        // October 2025 → 2025/26 session, first term current — only the current term is created.
        _repo.Verify(r => r.ProvisionCalendarAsync(School, 2025, Term.First,
            It.Is<IReadOnlyList<(Term, DateOnly, DateOnly)>>(t => t.Count == 1 && t[0].Item1 == Term.First),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_TermEnded_NextMissing_PreparesNextAndNotifiesOwner()
    {
        Schools(School);
        _repo.Setup(r => r.GetSnapshotAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(new CalendarSnapshotRow
        {
            HasYears = true, CurrentYearId = YearId, CurrentYearStartsIn = 2025, CurrentYearName = "2025/26",
            CurrentTermName = "first", CurrentTermEndDate = new DateOnly(2025, 12, 18)
        });
        _repo.Setup(r => r.TermExistsInYearAsync(School, YearId, Term.Second, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.GetOwnerPhoneAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync("+2348030000001");

        await CreateSut().RunAsync(new DateOnly(2026, 1, 2), CancellationToken.None);

        _repo.Verify(r => r.PrepareTermInYearAsync(School, YearId, Term.Second,
            It.IsAny<(DateOnly, DateOnly)?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.SendSmsAsync("+2348030000001", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // Preparation is the notification trigger — an already-prepared next term means the school was
    // told before, so a daily rerun must stay silent (no SMS spam).
    [Fact]
    public async Task Run_TermEnded_NextAlreadyPrepared_DoesNothing()
    {
        Schools(School);
        _repo.Setup(r => r.GetSnapshotAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(new CalendarSnapshotRow
        {
            HasYears = true, CurrentYearId = YearId, CurrentYearStartsIn = 2025, CurrentYearName = "2025/26",
            CurrentTermName = "first", CurrentTermEndDate = new DateOnly(2025, 12, 18)
        });
        _repo.Setup(r => r.TermExistsInYearAsync(School, YearId, Term.Second, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await CreateSut().RunAsync(new DateOnly(2026, 1, 2), CancellationToken.None);

        _repo.Verify(r => r.PrepareTermInYearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Term>(),
            It.IsAny<(DateOnly, DateOnly)?>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Run_ThirdTermEnded_PreparesNextSessionAndItsFirstTerm()
    {
        Schools(School);
        Guid nextYear = Guid.NewGuid();
        _repo.Setup(r => r.GetSnapshotAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(new CalendarSnapshotRow
        {
            HasYears = true, CurrentYearId = YearId, CurrentYearStartsIn = 2025, CurrentYearName = "2025/26",
            CurrentTermName = "third", CurrentTermEndDate = new DateOnly(2026, 7, 31)
        });
        _repo.Setup(r => r.EnsureYearAsync(School, "2026/27", 2026, It.IsAny<CancellationToken>())).ReturnsAsync(nextYear);
        _repo.Setup(r => r.TermExistsInYearAsync(School, nextYear, Term.First, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await CreateSut().RunAsync(new DateOnly(2026, 8, 15), CancellationToken.None);

        _repo.Verify(r => r.PrepareTermInYearAsync(School, nextYear, Term.First,
            It.IsAny<(DateOnly, DateOnly)?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_TermOngoing_DoesNothing()
    {
        Schools(School);
        _repo.Setup(r => r.GetSnapshotAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(new CalendarSnapshotRow
        {
            HasYears = true, CurrentYearId = YearId, CurrentYearStartsIn = 2025, CurrentYearName = "2025/26",
            CurrentTermName = "first", CurrentTermEndDate = new DateOnly(2025, 12, 18)
        });

        await CreateSut().RunAsync(new DateOnly(2025, 11, 1), CancellationToken.None);

        _repo.Verify(r => r.PrepareTermInYearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Term>(),
            It.IsAny<(DateOnly, DateOnly)?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // A school managing its calendar by hand (no term end date) is never auto-advanced.
    [Fact]
    public async Task Run_DatelessCurrentTerm_IsSkipped()
    {
        Schools(School);
        _repo.Setup(r => r.GetSnapshotAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(new CalendarSnapshotRow
        {
            HasYears = true, CurrentYearId = YearId, CurrentYearStartsIn = 2025, CurrentYearName = "2025/26",
            CurrentTermName = "first", CurrentTermEndDate = null
        });

        await CreateSut().RunAsync(new DateOnly(2026, 1, 2), CancellationToken.None);

        _repo.Verify(r => r.PrepareTermInYearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Term>(),
            It.IsAny<(DateOnly, DateOnly)?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // One school's failure must not stop the sweep for everyone else.
    [Fact]
    public async Task Run_OneSchoolThrows_OthersStillProcessed()
    {
        Guid broken = Guid.NewGuid();
        Schools(broken, School);
        _repo.Setup(r => r.GetSnapshotAsync(broken, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        _repo.Setup(r => r.GetSnapshotAsync(School, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarSnapshotRow { HasYears = false });

        await CreateSut().RunAsync(new DateOnly(2025, 10, 10), CancellationToken.None);

        _repo.Verify(r => r.ProvisionCalendarAsync(School, 2025, Term.First,
            It.IsAny<IReadOnlyList<(Term, DateOnly, DateOnly)>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
