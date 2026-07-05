using EduTech.Grades.Subjects;
using EduTech.Shared.Exceptions;
using Moq;

namespace EduTech.Auth.Tests.Grades;

public class SubjectServiceTests
{
    private readonly Mock<ISubjectRepository> _repo = new();

    private SubjectService CreateSut() => new(_repo.Object);

    private static readonly Guid ClassId = Guid.NewGuid();

    private void ClassExists() =>
        _repo.Setup(r => r.ClassExistsAsync(ClassId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    [Fact]
    public async Task Create_EmptyName_Throws400()
    {
        ClassExists();
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().CreateAsync(ClassId, new CreateSubjectRequest { Name = "  " }, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_ClassNotFound_Throws404()
    {
        _repo.Setup(r => r.ClassExistsAsync(ClassId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().CreateAsync(ClassId, new CreateSubjectRequest { Name = "Mathematics" }, CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateName_Throws409()
    {
        ClassExists();
        _repo.Setup(r => r.NameExistsAsync(ClassId, "Mathematics", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().CreateAsync(ClassId, new CreateSubjectRequest { Name = "Mathematics" }, CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_AppliesDefaultMaxes()
    {
        ClassExists();
        _repo.Setup(r => r.NameExistsAsync(ClassId, "Mathematics", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Guid id = Guid.NewGuid();
        _repo.Setup(r => r.CreateAsync(ClassId, "Mathematics", 30, 40, 0, It.IsAny<CancellationToken>())).ReturnsAsync(id);

        SubjectResponse res = await CreateSut().CreateAsync(ClassId, new CreateSubjectRequest { Name = "Mathematics" }, CancellationToken.None);

        Assert.Equal(30, res.MaxCa);
        Assert.Equal(40, res.MaxExam);
        _repo.Verify(r => r.CreateAsync(ClassId, "Mathematics", 30, 40, 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    // The grading scale runs 0–100, so a subject's assessment maxima must total 100
    // (CA1 + CA2 + Exam = 2×MaxCa + MaxExam). Anything else produces unresolvable totals.
    [Theory]
    [InlineData(30, 70)]    // 130 total
    [InlineData(10, 40)]    // 60 total
    public async Task Create_MaxesNotSummingTo100_Throws400(int maxCa, int maxExam)
    {
        ClassExists();
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().CreateAsync(ClassId,
                new CreateSubjectRequest { Name = "Mathematics", MaxCa = maxCa, MaxExam = maxExam },
                CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    // Out-of-range maxima must be rejected loudly, not silently replaced with the defaults.
    [Theory]
    [InlineData(0, 100)]
    [InlineData(101, 40)]
    public async Task Create_OutOfRangeMax_Throws400(int maxCa, int maxExam)
    {
        ClassExists();
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().CreateAsync(ClassId,
                new CreateSubjectRequest { Name = "Mathematics", MaxCa = maxCa, MaxExam = maxExam },
                CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_CustomValidSplit_Persists()
    {
        ClassExists();
        _repo.Setup(r => r.NameExistsAsync(ClassId, "Mathematics", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Guid id = Guid.NewGuid();
        _repo.Setup(r => r.CreateAsync(ClassId, "Mathematics", 15, 70, 0, It.IsAny<CancellationToken>())).ReturnsAsync(id);

        SubjectResponse res = await CreateSut().CreateAsync(ClassId,
            new CreateSubjectRequest { Name = "Mathematics", MaxCa = 15, MaxExam = 70 }, CancellationToken.None);

        Assert.Equal(15, res.MaxCa);
        Assert.Equal(70, res.MaxExam);
    }

    [Fact]
    public async Task Delete_NotFound_Throws404()
    {
        _repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().DeleteAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }
}
