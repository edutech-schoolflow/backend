using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using EduTech.Students.Admissions;
using Moq;

namespace EduTech.Auth.Tests.Admissions;

public class SchoolApplicationServiceTests
{
    private readonly Mock<ISchoolApplicationRepository> _repo = new();
    private readonly Mock<INotificationDispatcher> _sms = new();

    private SchoolApplicationService CreateSut() => new(_repo.Object, _sms.Object);

    private static readonly Guid App = Guid.NewGuid();
    private static readonly Guid Arm = Guid.NewGuid();

    private static ApplicationRow Row(string status, string? admission = null) => new ApplicationRow
    {
        Id = App, ReferenceNumber = "APP/2026/ABC123", ChildProfileId = Guid.NewGuid(),
        ChildFirstName = "Tunde", ChildLastName = "Johnson", ChildDateOfBirth = new DateOnly(2012, 2, 10),
        SchoolId = Guid.NewGuid(), ParentId = Guid.NewGuid(), Status = status, AdmissionNumber = admission,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private void NotifyTarget() =>
        _repo.Setup(r => r.GetNotifyTargetAsync(App, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationNotifyRow { Phone = "+2348030000001", ChildName = "Tunde Johnson" });

    [Fact]
    public async Task Admit_UnderReview_CreatesStudentAndNotifies()
    {
        _repo.Setup(r => r.ArmExistsAsync(Arm, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.GetStatusAsync(App, It.IsAny<CancellationToken>())).ReturnsAsync("under_review");
        _repo.Setup(r => r.AdmitAsync(App, ApplicationStatus.UnderReview, Arm, It.IsAny<CancellationToken>()))
            .ReturnsAsync("SCH/2026/005");
        _repo.Setup(r => r.GetAsync(App, It.IsAny<CancellationToken>())).ReturnsAsync(Row("admitted", "SCH/2026/005"));
        NotifyTarget();

        ApplicationResponse res = await CreateSut().AdmitAsync(App, new AdmitApplicationRequest { ClassArmId = Arm }, CancellationToken.None);

        Assert.Equal(ApplicationStatus.Admitted, res.Status);
        Assert.Equal("SCH/2026/005", res.AdmissionNumber);
        _repo.Verify(r => r.AdmitAsync(App, ApplicationStatus.UnderReview, Arm, It.IsAny<CancellationToken>()), Times.Once);
        _sms.Verify(s => s.SendSmsAsync("+2348030000001", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Admit_AlreadyAdmitted_Throws409()
    {
        _repo.Setup(r => r.ArmExistsAsync(Arm, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.GetStatusAsync(App, It.IsAny<CancellationToken>())).ReturnsAsync("admitted");

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().AdmitAsync(App, new AdmitApplicationRequest { ClassArmId = Arm }, CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
        _repo.Verify(r => r.AdmitAsync(It.IsAny<Guid>(), It.IsAny<ApplicationStatus>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Admit_RejectedApplication_Throws409()
    {
        _repo.Setup(r => r.ArmExistsAsync(Arm, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.GetStatusAsync(App, It.IsAny<CancellationToken>())).ReturnsAsync("rejected");

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().AdmitAsync(App, new AdmitApplicationRequest { ClassArmId = Arm }, CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Admit_MissingArm_Throws400()
    {
        _repo.Setup(r => r.ArmExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().AdmitAsync(App, new AdmitApplicationRequest { ClassArmId = Arm }, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Reject_UnderReview_Rejects()
    {
        _repo.Setup(r => r.GetStatusAsync(App, It.IsAny<CancellationToken>())).ReturnsAsync("under_review");
        _repo.Setup(r => r.RejectAsync(App, ApplicationStatus.UnderReview, "no space", It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _repo.Setup(r => r.GetAsync(App, It.IsAny<CancellationToken>())).ReturnsAsync(Row("rejected"));
        NotifyTarget();

        ApplicationResponse res = await CreateSut().RejectAsync(App, new RejectApplicationRequest { Reason = "no space" }, CancellationToken.None);

        Assert.Equal(ApplicationStatus.Rejected, res.Status);
        _repo.Verify(r => r.RejectAsync(App, ApplicationStatus.UnderReview, "no space", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScheduleExam_OnAdmitted_Throws409()
    {
        _repo.Setup(r => r.GetStatusAsync(App, It.IsAny<CancellationToken>())).ReturnsAsync("admitted");

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().ScheduleExamAsync(App, new ScheduleExamRequest { ExamVenue = "Hall" }, CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }
}
