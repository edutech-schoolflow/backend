using EduTech.Shared.Constants;
using EduTech.Shared.Events;
using EduTech.Students.Academics.Transition;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EduTech.Auth.Tests.Students;

public class SchoolCalendarProvisionerTests
{
    private readonly Mock<ICalendarRollForwardRepository> _repo = new();

    private SchoolCalendarProvisioner CreateSut() => new(_repo.Object);

    private static readonly Guid School = Guid.NewGuid();

    [Fact]
    public async Task ProvisionIfMissing_NoCalendar_ProvisionsCurrentSession()
    {
        _repo.Setup(r => r.GetSnapshotAsync(School, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarSnapshotRow { HasYears = false });

        bool provisioned = await CreateSut()
            .ProvisionIfMissingAsync(School, new DateOnly(2025, 10, 10), CancellationToken.None);

        Assert.True(provisioned);
        // October 2025 → 2025/26 session, First term current — ONLY the current term, no past terms.
        _repo.Verify(r => r.ProvisionCalendarAsync(School, 2025, Term.First,
            It.Is<IReadOnlyList<(Term, DateOnly, DateOnly)>>(t => t.Count == 1 && t[0].Item1 == Term.First),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionIfMissing_AlreadyHasCalendar_DoesNothing()
    {
        _repo.Setup(r => r.GetSnapshotAsync(School, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarSnapshotRow { HasYears = true });

        bool provisioned = await CreateSut()
            .ProvisionIfMissingAsync(School, new DateOnly(2025, 10, 10), CancellationToken.None);

        Assert.False(provisioned);
        _repo.Verify(r => r.ProvisionCalendarAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Term>(),
            It.IsAny<IReadOnlyList<(Term, DateOnly, DateOnly)>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handler_OnSchoolActivated_ProvisionsViaProvisioner()
    {
        Mock<ISchoolCalendarProvisioner> provisioner = new();
        ProvisionCalendarOnSchoolActivated handler =
            new(provisioner.Object, NullLogger<ProvisionCalendarOnSchoolActivated>.Instance);

        await handler.HandleAsync(new SchoolActivatedEvent(School), CancellationToken.None);

        provisioner.Verify(p => p.ProvisionIfMissingAsync(School, It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
