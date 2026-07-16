using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Workforce.StaffAttendance;
using Moq;

namespace EduTech.Auth.Tests.Workforce;

/// <summary>Geofenced staff check-in rules: the fence, the cutoff, and the owner-only override.</summary>
public class StaffAttendanceServiceTests
{
    private readonly Mock<IStaffAttendanceRepository> _repo = new();
    private readonly Mock<IEduTechRequestContext> _context = new();

    private static readonly Guid School = Guid.NewGuid();
    private static readonly Guid Affiliation = Guid.NewGuid();

    private StaffAttendanceService CreateSut() => new(_repo.Object, _context.Object);

    private void StaffSession()
    {
        _context.SetupGet(c => c.SchoolId).Returns(School.ToString());
        _context.SetupGet(c => c.AffiliationId).Returns(Affiliation.ToString());
        _context.SetupGet(c => c.IsOwner).Returns(false);
    }

    [Fact]
    public async Task CheckIn_OutsideTheFence_Throws400WithDistance()
    {
        StaffSession();
        _repo.Setup(r => r.GetSettingsAsync(School, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettingsRow { Lat = 9.0608, Lng = 7.4896, GeofenceRadiusM = 200 });

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() =>
            CreateSut().CheckInAsync(new CheckInRequest { Lat = 6.5244, Lng = 3.3792 }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        _repo.Verify(r => r.UpsertCheckInAsync(It.IsAny<CheckInRow>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckIn_InsideTheFence_RecordsWithDistance()
    {
        StaffSession();
        _repo.Setup(r => r.GetSettingsAsync(School, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettingsRow { Lat = 9.0608, Lng = 7.4896, GeofenceRadiusM = 200,
                CheckInCutoff = new TimeSpan(23, 59, 0) }); // generous cutoff → deterministic "present"
        _repo.Setup(r => r.UpsertCheckInAsync(It.IsAny<CheckInRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckInRow r, CancellationToken _) => r);

        StaffCheckInResponse record = await CreateSut()
            .CheckInAsync(new CheckInRequest { Lat = 9.0609, Lng = 7.4897 }, CancellationToken.None);

        Assert.Equal("present", record.Status);
        Assert.InRange(record.DistanceMeters, 1, 200);
    }

    [Fact]
    public async Task CheckIn_AfterCutoff_IsLate()
    {
        StaffSession();
        _repo.Setup(r => r.GetSettingsAsync(School, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettingsRow { CheckInCutoff = TimeSpan.Zero }); // 00:00 → always late
        _repo.Setup(r => r.UpsertCheckInAsync(It.IsAny<CheckInRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckInRow r, CancellationToken _) => r);

        StaffCheckInResponse record = await CreateSut()
            .CheckInAsync(new CheckInRequest { Lat = 1, Lng = 1 }, CancellationToken.None);

        Assert.Equal("late", record.Status);
    }

    [Fact]
    public async Task CheckIn_NoLocationConfigured_AcceptsWithoutFence()
    {
        StaffSession();
        _repo.Setup(r => r.GetSettingsAsync(School, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsRow?)null);
        _repo.Setup(r => r.UpsertCheckInAsync(It.IsAny<CheckInRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckInRow r, CancellationToken _) => r);

        StaffCheckInResponse record = await CreateSut()
            .CheckInAsync(new CheckInRequest { Lat = 6.5, Lng = 3.3 }, CancellationToken.None);

        Assert.Equal(0, record.DistanceMeters);
    }

    [Fact]
    public async Task Override_NonOwner_Throws403()
    {
        StaffSession();

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() =>
            CreateSut().OverrideAsync(new OverrideStaffAttendanceRequest
            {
                StaffId = Affiliation, Date = new DateOnly(2026, 7, 15), Status = "absent"
            }, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Override_InvalidStatus_Throws400()
    {
        _context.SetupGet(c => c.SchoolId).Returns(School.ToString());
        _context.SetupGet(c => c.IsOwner).Returns(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() =>
            CreateSut().OverrideAsync(new OverrideStaffAttendanceRequest
            {
                StaffId = Affiliation, Date = new DateOnly(2026, 7, 15), Status = "vacation"
            }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }
}
