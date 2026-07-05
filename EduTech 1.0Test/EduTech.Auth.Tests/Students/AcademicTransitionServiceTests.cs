using TermName = EduTech.Shared.Constants.Term;
using EduTech.Shared.Exceptions;
using EduTech.Students.Academics;
using EduTech.Students.Academics.Transition;
using Moq;

namespace EduTech.Auth.Tests.Students;

public class AcademicTransitionServiceTests
{
    private readonly Mock<IAcademicCalendarRepository> _repo = new();
    private readonly Mock<IAcademicCalendarService> _calendar = new();

    private AcademicTransitionService CreateSut() => new(_repo.Object, _calendar.Object);

    private static readonly Guid Year2025 = Guid.NewGuid();
    private static readonly Guid Year2026 = Guid.NewGuid();
    private static readonly Guid FirstTerm = Guid.NewGuid();
    private static readonly Guid SecondTerm = Guid.NewGuid();
    private static readonly Guid ThirdTerm = Guid.NewGuid();
    private static readonly Guid NextFirstTerm = Guid.NewGuid();

    private void Year(Guid id, int startsIn, string name, bool current = true) =>
        _repo.Setup(r => r.GetYearAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcademicYearRow { Id = id, Name = name, StartsIn = startsIn, IsCurrent = current });

    private void Years(params AcademicYearRow[] rows) =>
        _repo.Setup(r => r.ListYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rows);

    private void Terms(params TermRow[] rows) =>
        _repo.Setup(r => r.ListTermsAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(rows);

    private static TermRow Term(Guid id, Guid yearId, string name, DateOnly? end, bool current = false) =>
        new TermRow { Id = id, AcademicYearId = yearId, Name = name, EndDate = end, IsCurrent = current };

    // ---- proposal ----

    [Fact]
    public async Task GetProposal_NoCurrentTerm_ReportsIt()
    {
        Terms();

        TransitionProposalResponse res = await CreateSut().GetProposalAsync(new DateOnly(2026, 1, 5), CancellationToken.None);

        Assert.Equal(TransitionStatus.NoCurrentTerm, res.Status);
    }

    [Fact]
    public async Task GetProposal_TermStillRunning_NothingDue()
    {
        Year(Year2025, 2025, "2025/26");
        Terms(Term(FirstTerm, Year2025, "first", new DateOnly(2025, 12, 18), current: true));

        TransitionProposalResponse res = await CreateSut().GetProposalAsync(new DateOnly(2025, 11, 1), CancellationToken.None);

        Assert.Equal(TransitionStatus.TermOngoing, res.Status);
    }

    [Fact]
    public async Task GetProposal_FirstTermEnded_ProposesSecondTerm()
    {
        Year(Year2025, 2025, "2025/26");
        Terms(
            Term(FirstTerm, Year2025, "first", new DateOnly(2025, 12, 18), current: true),
            Term(SecondTerm, Year2025, "second", null));

        TransitionProposalResponse res = await CreateSut().GetProposalAsync(new DateOnly(2026, 1, 2), CancellationToken.None);

        Assert.Equal(TransitionStatus.TransitionDue, res.Status);
        Assert.Equal(TermName.Second, res.NextTerm);
        Assert.False(res.IsSessionBoundary);
        Assert.True(res.NextTermPrepared);
        Assert.Equal(SecondTerm, res.NextTermId);
    }

    [Fact]
    public async Task GetProposal_ThirdTermEnded_ProposesSessionRoll()
    {
        Year(Year2025, 2025, "2025/26");
        Years(new AcademicYearRow { Id = Year2025, Name = "2025/26", StartsIn = 2025, IsCurrent = true });
        Terms(Term(ThirdTerm, Year2025, "third", new DateOnly(2026, 7, 31), current: true));

        TransitionProposalResponse res = await CreateSut().GetProposalAsync(new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(TransitionStatus.TransitionDue, res.Status);
        Assert.Equal(TermName.First, res.NextTerm);
        Assert.True(res.IsSessionBoundary);
        Assert.False(res.NextTermPrepared);
        Assert.Equal(2026, res.NextSessionStartYear);
    }

    // ---- confirm ----

    [Fact]
    public async Task Confirm_TermStillRunning_Throws409()
    {
        Year(Year2025, 2025, "2025/26");
        Terms(Term(FirstTerm, Year2025, "first", new DateOnly(2025, 12, 18), current: true));

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().ConfirmAsync(new DateOnly(2025, 11, 1), CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Confirm_WithinSession_SetsCurrentTerm()
    {
        Year(Year2025, 2025, "2025/26");
        Terms(
            Term(FirstTerm, Year2025, "first", new DateOnly(2025, 12, 18), current: true),
            Term(SecondTerm, Year2025, "second", null));

        await CreateSut().ConfirmAsync(new DateOnly(2026, 1, 2), CancellationToken.None);

        _repo.Verify(r => r.SetCurrentTermAsync(SecondTerm, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SetCurrentYearAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_WithinSession_CreatesMissingNextTerm()
    {
        Year(Year2025, 2025, "2025/26");
        Terms(Term(FirstTerm, Year2025, "first", new DateOnly(2025, 12, 18), current: true));
        _calendar.Setup(c => c.CreateTermAsync(
                It.Is<CreateTermRequest>(t => t.AcademicYearId == Year2025 && t.Name == TermName.Second),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TermResponse { Id = SecondTerm, AcademicYearId = Year2025, Name = TermName.Second, IsCurrent = false });

        await CreateSut().ConfirmAsync(new DateOnly(2026, 1, 2), CancellationToken.None);

        _repo.Verify(r => r.SetCurrentTermAsync(SecondTerm, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Crossing a session boundary is coupled to promotion: enrollments live per session, so moving
    // the school into a session its students aren't enrolled in yet must be blocked, not allowed.
    [Fact]
    public async Task Confirm_SessionRoll_BlockedWhileStudentsAwaitPromotion()
    {
        Year(Year2025, 2025, "2025/26");
        Years(
            new AcademicYearRow { Id = Year2025, Name = "2025/26", StartsIn = 2025, IsCurrent = true },
            new AcademicYearRow { Id = Year2026, Name = "2026/27", StartsIn = 2026 });
        Terms(
            Term(ThirdTerm, Year2025, "third", new DateOnly(2026, 7, 31), current: true),
            Term(NextFirstTerm, Year2026, "first", null));
        _repo.Setup(r => r.CountActiveStudentsNotInYearAsync(Year2026, It.IsAny<CancellationToken>())).ReturnsAsync(12);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().ConfirmAsync(new DateOnly(2026, 9, 10), CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        _repo.Verify(r => r.SetCurrentYearAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_SessionRoll_AllPromoted_MovesYearAndTerm()
    {
        Year(Year2025, 2025, "2025/26");
        Years(
            new AcademicYearRow { Id = Year2025, Name = "2025/26", StartsIn = 2025, IsCurrent = true },
            new AcademicYearRow { Id = Year2026, Name = "2026/27", StartsIn = 2026 });
        Terms(
            Term(ThirdTerm, Year2025, "third", new DateOnly(2026, 7, 31), current: true),
            Term(NextFirstTerm, Year2026, "first", null));
        _repo.Setup(r => r.CountActiveStudentsNotInYearAsync(Year2026, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await CreateSut().ConfirmAsync(new DateOnly(2026, 9, 10), CancellationToken.None);

        _repo.Verify(r => r.SetCurrentYearAsync(Year2026, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SetCurrentTermAsync(NextFirstTerm, It.IsAny<CancellationToken>()), Times.Once);
    }
}
