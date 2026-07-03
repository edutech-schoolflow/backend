using EduTech.Attendance;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using Moq;

namespace EduTech.Auth.Tests.Attendance;

public class AttendanceServiceTests
{
    private readonly Mock<IAttendanceRepository> _repo = new();
    private readonly Mock<IEduTechRequestContext> _context = new();

    private AttendanceService CreateSut() => new(_repo.Object, _context.Object);

    private static readonly Guid Arm = Guid.NewGuid();
    private static readonly Guid TeacherAffiliation = Guid.NewGuid();
    private static readonly Guid StudentA = Guid.NewGuid();
    private static readonly Guid StudentB = Guid.NewGuid();
    private static readonly DateOnly Yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

    private void AsOwner()
    {
        _context.SetupGet(c => c.IsOwner).Returns(true);
        _context.SetupGet(c => c.AffiliationId).Returns((string?)null);
    }

    private void AsClassTeacher(Guid affiliation)
    {
        _context.SetupGet(c => c.IsOwner).Returns(false);
        _context.SetupGet(c => c.AffiliationId).Returns(affiliation.ToString());
    }

    private void ArmExists(Guid? classTeacher) =>
        _repo.Setup(r => r.GetArmAsync(Arm, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArmInfoRow { Id = Arm, ArmName = "JSS 1A", ClassTeacherAffiliationId = classTeacher });

    private static SubmitAttendanceRequest ValidSubmit() => new SubmitAttendanceRequest
    {
        ArmId = Arm,
        Date = Yesterday,
        Marks = new List<AttendanceMarkInput>
        {
            new AttendanceMarkInput { StudentId = StudentA, Status = AttendanceStatus.Present },
            new AttendanceMarkInput { StudentId = StudentB, Status = AttendanceStatus.Absent }
        }
    };

    private void RosterHas(params Guid[] studentIds) =>
        _repo.Setup(r => r.GetActiveStudentIdsAsync(Arm, It.IsAny<CancellationToken>()))
            .ReturnsAsync(studentIds);

    // ---- authorization ----

    [Fact]
    public async Task Submit_ArmNotFound_Throws404()
    {
        AsOwner();
        _repo.Setup(r => r.GetArmAsync(Arm, It.IsAny<CancellationToken>())).ReturnsAsync((ArmInfoRow?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_NotClassTeacher_Throws403()
    {
        AsClassTeacher(Guid.NewGuid());           // some other affiliation
        ArmExists(TeacherAffiliation);            // arm owned by a different teacher

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_OwnerAnyArm_Succeeds()
    {
        AsOwner();
        ArmExists(TeacherAffiliation);            // owner doesn't need to be the class teacher
        RosterHas(StudentA, StudentB);
        _repo.Setup(r => r.GetCurrentTermIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        Guid recordId = Guid.NewGuid();
        _repo.Setup(r => r.UpsertRecordAsync(Arm, Yesterday, null, null,
                It.IsAny<IReadOnlyList<(Guid, AttendanceStatus)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((recordId, DateTime.UtcNow));

        AttendanceRecordResponse res = await CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None);

        Assert.Equal(recordId, res.Id);
        Assert.Equal(1, res.PresentCount);
        Assert.Equal(1, res.AbsentCount);
        Assert.Equal(2, res.TotalCount);
    }

    [Fact]
    public async Task Submit_ClassTeacherOfArm_PassesOwnAffiliationAsSubmitter()
    {
        AsClassTeacher(TeacherAffiliation);
        ArmExists(TeacherAffiliation);
        RosterHas(StudentA, StudentB);
        _repo.Setup(r => r.GetCurrentTermIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        _repo.Setup(r => r.UpsertRecordAsync(Arm, Yesterday, null, TeacherAffiliation,
                It.IsAny<IReadOnlyList<(Guid, AttendanceStatus)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), DateTime.UtcNow));

        await CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None);

        _repo.Verify(r => r.UpsertRecordAsync(Arm, Yesterday, null, TeacherAffiliation,
            It.IsAny<IReadOnlyList<(Guid, AttendanceStatus)>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- validation ----

    [Fact]
    public async Task Submit_FutureDate_Throws400()
    {
        AsOwner();
        ArmExists(TeacherAffiliation);
        SubmitAttendanceRequest req = ValidSubmit();
        req = new SubmitAttendanceRequest { ArmId = Arm, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Marks = req.Marks };

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_EmptyMarks_Throws400()
    {
        AsOwner();
        ArmExists(TeacherAffiliation);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(new SubmitAttendanceRequest { ArmId = Arm, Date = Yesterday }, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_MissingStatus_Throws400()
    {
        // Status omitted → null → service rejects (an invalid status is unrepresentable with the enum).
        AsOwner();
        ArmExists(TeacherAffiliation);
        RosterHas(StudentA);
        SubmitAttendanceRequest req = new SubmitAttendanceRequest
        {
            ArmId = Arm, Date = Yesterday,
            Marks = new List<AttendanceMarkInput> { new AttendanceMarkInput { StudentId = StudentA } }
        };

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_StudentNotInArm_Throws400()
    {
        AsOwner();
        ArmExists(TeacherAffiliation);
        RosterHas(StudentA);                      // StudentB is not in the arm

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_DuplicateStudent_Throws400()
    {
        AsOwner();
        ArmExists(TeacherAffiliation);
        RosterHas(StudentA);
        SubmitAttendanceRequest req = new SubmitAttendanceRequest
        {
            ArmId = Arm, Date = Yesterday,
            Marks = new List<AttendanceMarkInput>
            {
                new AttendanceMarkInput { StudentId = StudentA, Status = AttendanceStatus.Present },
                new AttendanceMarkInput { StudentId = StudentA, Status = AttendanceStatus.Absent }
            }
        };

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    // ---- roster + arms ----

    [Fact]
    public async Task ListMarkableArms_ForwardsAffiliationAndOwnerFlag()
    {
        AsClassTeacher(TeacherAffiliation);
        _repo.Setup(r => r.ListMarkableArmsAsync(TeacherAffiliation, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new MarkableArmRow { ArmId = Arm, ArmName = "JSS 1A", ClassId = Guid.NewGuid(), ClassName = "JSS 1", Level = "junior_secondary" } });

        IReadOnlyList<MarkableArmResponse> arms = await CreateSut().ListMarkableArmsAsync(CancellationToken.None);

        Assert.Single(arms);
        Assert.Equal("JSS 1A", arms[0].ArmName);
        _repo.Verify(r => r.ListMarkableArmsAsync(TeacherAffiliation, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Roster_SetsSubmittedFromRecordExists()
    {
        AsOwner();
        ArmExists(TeacherAffiliation);
        _repo.Setup(r => r.GetRosterAsync(Arm, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RosterStudentRow { StudentId = StudentA, StudentName = "Tolu Adebayo", Status = "present" } });
        _repo.Setup(r => r.RecordExistsAsync(Arm, Yesterday, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        AttendanceRosterResponse res = await CreateSut().GetRosterAsync(Arm, Yesterday, CancellationToken.None);

        Assert.True(res.Submitted);
        Assert.Single(res.Students);
        Assert.Equal(AttendanceStatus.Present, res.Students[0].Status);
    }

    // ---- overview ----

    [Fact]
    public async Task Overview_ComputesPercentsAndTotalsFromSubmittedArmsOnly()
    {
        AsOwner();
        DateOnly date = Yesterday;
        _repo.Setup(r => r.GetOverviewArmStatsAsync(date, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new ArmStatRow { ArmId = Guid.NewGuid(), ArmName = "JSS 1A", Submitted = true,  PresentCount = 18, AbsentCount = 2, LateCount = 0, TotalCount = 20 },
            new ArmStatRow { ArmId = Guid.NewGuid(), ArmName = "JSS 1B", Submitted = false, PresentCount = 0,  AbsentCount = 0, LateCount = 0, TotalCount = 25 }
        });
        _repo.Setup(r => r.GetAbsentStudentsAsync(date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AbsentStudentRow { StudentName = "Ada Obi", ArmName = "JSS 1A" } });

        AttendanceOverviewResponse res = await CreateSut().GetOverviewAsync(date, CancellationToken.None);

        Assert.Equal(18, res.TotalPresent);
        Assert.Equal(2, res.TotalAbsent);
        Assert.Equal(20, res.TotalStudents);          // the unsubmitted arm contributes nothing
        Assert.Equal(90, res.OverallPresentPct);      // 18 / 20
        Assert.Equal(90, res.Arms.First(a => a.ArmName == "JSS 1A").PresentPct);
        Assert.Equal(0, res.Arms.First(a => a.ArmName == "JSS 1B").PresentPct);
        Assert.Single(res.AbsentStudents);
    }
}
