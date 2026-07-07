using EduTech.Shared.Constants;
using EduTech.Shared.Events;
using EduTech.Students.Classes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EduTech.Auth.Tests.Students;

public class SchoolClassProvisionerTests
{
    private readonly Mock<ISchoolClassProvisionRepository> _repo = new();
    private SchoolClassProvisioner CreateSut() => new(_repo.Object);
    private static readonly Guid School = Guid.NewGuid();

    [Fact]
    public async Task ProvisionIfMissing_PrimarySchool_CreatesPrimary1To6()
    {
        _repo.Setup(r => r.HasClassesAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.GetSchoolTypeAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync("primary");

        bool provisioned = await CreateSut().ProvisionIfMissingAsync(School, CancellationToken.None);

        Assert.True(provisioned);
        _repo.Verify(r => r.CreateClassesAsync(School,
            It.Is<IReadOnlyList<(string, ClassLevel, int)>>(c =>
                c.Count == 6 && c.All(x => x.Item2 == ClassLevel.Primary)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionIfMissing_Combined_CreatesFullLadder()
    {
        _repo.Setup(r => r.HasClassesAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.GetSchoolTypeAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync("combined");

        await CreateSut().ProvisionIfMissingAsync(School, CancellationToken.None);

        _repo.Verify(r => r.CreateClassesAsync(School,
            It.Is<IReadOnlyList<(string, ClassLevel, int)>>(c => c.Count == 14),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionIfMissing_AlreadyHasClasses_DoesNothing()
    {
        _repo.Setup(r => r.HasClassesAsync(School, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        bool provisioned = await CreateSut().ProvisionIfMissingAsync(School, CancellationToken.None);

        Assert.False(provisioned);
        _repo.Verify(r => r.CreateClassesAsync(It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<(string, ClassLevel, int)>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handler_OnSchoolActivated_ProvisionsClasses()
    {
        Mock<ISchoolClassProvisioner> provisioner = new();
        ProvisionClassesOnSchoolActivated handler =
            new(provisioner.Object, NullLogger<ProvisionClassesOnSchoolActivated>.Instance);

        await handler.HandleAsync(new SchoolActivatedEvent(School), CancellationToken.None);

        provisioner.Verify(p => p.ProvisionIfMissingAsync(School, It.IsAny<CancellationToken>()), Times.Once);
    }
}
