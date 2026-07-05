using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Students.Academics;
using Moq;

namespace EduTech.Auth.Tests.Students;

public class AcademicCalendarServiceTests
{
    private readonly Mock<IAcademicCalendarRepository> _repo = new();

    private AcademicCalendarService CreateSut() => new(_repo.Object);

    [Fact]
    public async Task CreateYear_ZeroYear_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateYearAsync(
            new CreateAcademicYearRequest { StartYear = 0, EndYear = 2025 }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        _repo.Verify(r => r.CreateYearAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateYear_Valid_PersistsDerivedSessionName()
    {
        Guid id = Guid.NewGuid();
        _repo.Setup(r => r.CreateYearAsync("2024/25", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((id, false));

        AcademicYearResponse res = await CreateSut().CreateYearAsync(
            new CreateAcademicYearRequest { StartYear = 2024, EndYear = 2025 }, CancellationToken.None);

        Assert.Equal(id, res.Id);
        Assert.Equal("2024/25", res.Name);
        Assert.Equal(2024, res.StartYear);
        Assert.Equal(2025, res.EndYear);
        Assert.False(res.IsCurrent);
    }

    [Fact]
    public async Task CreateYear_DurationNotOneYear_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateYearAsync(
            new CreateAcademicYearRequest { StartYear = 2024, EndYear = 2027 }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        _repo.Verify(r => r.CreateYearAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListYears_MapsStartAndEndYearsFromSessionName()
    {
        Guid id = Guid.NewGuid();
        _repo.Setup(r => r.ListYearsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AcademicYearRow>
            {
                new() { Id = id, Name = "2024/25", IsCurrent = true }
            });

        IReadOnlyList<AcademicYearResponse> years = await CreateSut().ListYearsAsync(CancellationToken.None);

        AcademicYearResponse res = Assert.Single(years);
        Assert.Equal(id, res.Id);
        Assert.Equal(2024, res.StartYear);
        Assert.Equal(2025, res.EndYear);
        Assert.True(res.IsCurrent);
    }

    [Fact]
    public async Task SetCurrentYear_NotFound_Throws404()
    {
        _repo.Setup(r => r.YearExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SetCurrentYearAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
        _repo.Verify(r => r.SetCurrentYearAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateTerm_MissingName_Throws400()
    {
        // Name omitted → null → service rejects (an invalid term is unrepresentable with the enum).
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateTermAsync(
            new CreateTermRequest { AcademicYearId = Guid.NewGuid() }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task CreateTerm_YearNotFound_Throws404()
    {
        _repo.Setup(r => r.YearExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateTermAsync(
            new CreateTermRequest { AcademicYearId = Guid.NewGuid(), Name = Term.First }, CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task CreateTerm_EndBeforeStart_Throws400()
    {
        _repo.Setup(r => r.GetYearAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcademicYearRow { Id = Guid.NewGuid(), Name = "2025/2026", StartsIn = 2025 });
        _repo.Setup(r => r.ListTermsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TermRow>());

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateTermAsync(
            new CreateTermRequest
            {
                AcademicYearId = Guid.NewGuid(), Name = Term.First,
                StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2025, 8, 1)
            },
            CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task CreateTerm_Valid_Creates()
    {
        Guid yearId = Guid.NewGuid();
        Guid termId = Guid.NewGuid();
        _repo.Setup(r => r.GetYearAsync(yearId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcademicYearRow { Id = yearId, Name = "2025/2026", StartsIn = 2025 });
        _repo.Setup(r => r.ListTermsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TermRow>());
        _repo.Setup(r => r.CreateTermAsync(yearId, Term.First, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((termId, false));

        TermResponse res = await CreateSut().CreateTermAsync(
            new CreateTermRequest { AcademicYearId = yearId, Name = Term.First }, CancellationToken.None);

        Assert.Equal(termId, res.Id);
        Assert.Equal(Term.First, res.Name);
    }

    [Fact]
    public async Task UpdateYear_OngoingSession_Throws409()
    {
        // Editing the current/ongoing session would shift the timeline under live terms/results — block it.
        Guid yearId = Guid.NewGuid();
        _repo.Setup(r => r.GetYearAsync(yearId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcademicYearRow { Id = yearId, Name = "2025/26", StartsIn = 2025, IsCurrent = true });

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().UpdateYearAsync(
            yearId, new UpdateAcademicYearRequest { StartYear = 2026, EndYear = 2027 }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateYear_SessionWithTerms_Throws409()
    {
        // The "already has terms" lock is now enforced by the AcademicSession aggregate from its loaded terms.
        Guid yearId = Guid.NewGuid();
        _repo.Setup(r => r.GetYearAsync(yearId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcademicYearRow { Id = yearId, Name = "2025/26", StartsIn = 2025, IsCurrent = false });
        _repo.Setup(r => r.ListTermsAsync(yearId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TermRow>
            {
                new TermRow { Id = Guid.NewGuid(), AcademicYearId = yearId, Name = "first" }
            });

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().UpdateYearAsync(
            yearId, new UpdateAcademicYearRequest { StartYear = 2026, EndYear = 2027 }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task CreateTerm_DatesOutsideSessionWindow_Throws400()
    {
        // A 2025/2026 session (starts_in = 2025) must not accept a term ending in 2027.
        Guid yearId = Guid.NewGuid();
        _repo.Setup(r => r.GetYearAsync(yearId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcademicYearRow { Id = yearId, Name = "2025/2026", StartsIn = 2025 });
        _repo.Setup(r => r.ListTermsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TermRow>());

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateTermAsync(
            new CreateTermRequest
            {
                AcademicYearId = yearId, Name = Term.First,
                StartDate = new DateOnly(2025, 12, 11), EndDate = new DateOnly(2027, 11, 11)
            },
            CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }
}
