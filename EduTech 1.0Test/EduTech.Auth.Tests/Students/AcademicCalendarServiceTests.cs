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
    public async Task CreateYear_EmptyName_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateYearAsync(
            new CreateAcademicYearRequest { Name = "   " }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        _repo.Verify(r => r.CreateYearAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateYear_Valid_PersistsTrimmed()
    {
        Guid id = Guid.NewGuid();
        _repo.Setup(r => r.CreateYearAsync("2024/2025", It.IsAny<CancellationToken>())).ReturnsAsync(id);

        AcademicYearResponse res = await CreateSut().CreateYearAsync(
            new CreateAcademicYearRequest { Name = " 2024/2025 " }, CancellationToken.None);

        Assert.Equal(id, res.Id);
        Assert.Equal("2024/2025", res.Name);
        Assert.False(res.IsCurrent);
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
        _repo.Setup(r => r.YearExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

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
        _repo.Setup(r => r.YearExistsAsync(yearId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.CreateTermAsync(yearId, Term.First, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(termId);

        TermResponse res = await CreateSut().CreateTermAsync(
            new CreateTermRequest { AcademicYearId = yearId, Name = Term.First }, CancellationToken.None);

        Assert.Equal(termId, res.Id);
        Assert.Equal(Term.First, res.Name);
    }
}
