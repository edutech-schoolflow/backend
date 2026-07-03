using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Students.Admissions;
using Moq;

namespace EduTech.Auth.Tests.Admissions;

public class ParentApplicationServiceTests
{
    private readonly Mock<IParentApplicationRepository> _repo = new();
    private readonly Mock<IEduTechRequestContext> _context = new();

    private static readonly Guid Parent = Guid.NewGuid();
    private static readonly Guid Child = Guid.NewGuid();
    private static readonly Guid School = Guid.NewGuid();

    private ParentApplicationService CreateSut()
    {
        _context.SetupGet(c => c.UserId).Returns(Parent.ToString());
        return new ParentApplicationService(_repo.Object, _context.Object);
    }

    private static SubmitApplicationRequest Req() =>
        new SubmitApplicationRequest { ChildProfileId = Child, SchoolId = School, DesiredClass = "JSS 1" };

    private static ApplicationRow Row() => new ApplicationRow
    {
        Id = Guid.NewGuid(), ReferenceNumber = "APP/2026/ABC123", ChildProfileId = Child,
        ChildFirstName = "Ada", ChildLastName = "Obi", ChildDateOfBirth = new DateOnly(2013, 1, 1),
        SchoolId = School, ParentId = Parent, Status = "under_review",
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Submit_NotOwnedChild_Throws404()
    {
        _repo.Setup(r => r.ParentOwnsChildAsync(Parent, Child, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(Req(), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_ChildAlreadyEnrolledThere_Throws409()
    {
        _repo.Setup(r => r.ParentOwnsChildAsync(Parent, Child, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.SchoolExistsAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.ChildActiveAtSchoolAsync(Child, School, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(Req(), CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_DuplicateOpenApplication_Throws409()
    {
        _repo.Setup(r => r.ParentOwnsChildAsync(Parent, Child, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.SchoolExistsAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.ChildActiveAtSchoolAsync(Child, School, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.HasOpenApplicationAsync(Child, School, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(Req(), CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_Valid_ReturnsUnderReviewApplication()
    {
        _repo.Setup(r => r.ParentOwnsChildAsync(Parent, Child, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.SchoolExistsAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.ChildActiveAtSchoolAsync(Child, School, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.HasOpenApplicationAsync(Child, School, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.SubmitAsync(Parent, Child, School, "JSS 1", null, It.IsAny<CancellationToken>())).ReturnsAsync(Row());

        ApplicationResponse res = await CreateSut().SubmitAsync(Req(), CancellationToken.None);

        Assert.Equal(ApplicationStatus.UnderReview, res.Status);
        Assert.Equal("APP/2026/ABC123", res.ReferenceNumber);
    }
}
