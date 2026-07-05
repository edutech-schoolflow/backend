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

    private static readonly Guid Cls = Guid.NewGuid();
    private static readonly Guid Arm = Guid.NewGuid();
    private static readonly Guid Term = Guid.NewGuid();
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

    // The register unit is a class with an optional arm; the class teacher of the unit may mark it.
    private void UnitExists(Guid? classTeacher) =>
        _repo.Setup(r => r.GetUnitAsync(Cls, Arm, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnitInfoRow { ClassId = Cls, ArmId = Arm, UnitName = "JSS 1A", ClassTeacherAffiliationId = classTeacher });

    private static SubmitAttendanceRequest ValidSubmit() => new SubmitAttendanceRequest
    {
        ClassId = Cls,
        ArmId = Arm,
        Date = Yesterday,
        Marks = new List<AttendanceMarkInput>
        {
            new AttendanceMarkInput { StudentId = StudentA, Status = AttendanceStatus.Present },
            new AttendanceMarkInput { StudentId = StudentB, Status = AttendanceStatus.Absent }
        }
    };

    private void RosterHas(params Guid[] studentIds) =>
        _repo.Setup(r => r.GetActiveStudentIdsAsync(Cls, Arm, It.IsAny<CancellationToken>()))
            .ReturnsAsync(studentIds);

    // ---- authorization ----

    [Fact]
    public async Task Submit_UnitNotFound_Throws404()
    {
        AsOwner();
        _repo.Setup(r => r.GetUnitAsync(Cls, Arm, It.IsAny<CancellationToken>())).ReturnsAsync((UnitInfoRow?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_NotClassTeacher_Throws403()
    {
        AsClassTeacher(Guid.NewGuid());           // some other affiliation
        UnitExists(TeacherAffiliation);           // unit owned by a different teacher

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_OwnerAnyUnit_Succeeds()
    {
        AsOwner();
        UnitExists(TeacherAffiliation);           // owner doesn't need to be the class teacher
        RosterHas(StudentA, StudentB);
        _repo.Setup(r => r.GetCurrentTermIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Term);
        Guid recordId = Guid.NewGuid();
        _repo.Setup(r => r.UpsertRecordAsync(Cls, Arm, Yesterday, Term, null,
                It.IsAny<IReadOnlyList<(Guid, AttendanceStatus)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((recordId, DateTime.UtcNow));

        AttendanceRecordResponse res = await CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None);

        Assert.Equal(recordId, res.Id);
        Assert.Equal(1, res.PresentCount);
        Assert.Equal(1, res.AbsentCount);
        Assert.Equal(2, res.TotalCount);
    }

    [Fact]
    public async Task Submit_ClassTeacherOfUnit_PassesOwnAffiliationAsSubmitter()
    {
        AsClassTeacher(TeacherAffiliation);
        UnitExists(TeacherAffiliation);
        RosterHas(StudentA, StudentB);
        _repo.Setup(r => r.GetCurrentTermIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Term);
        _repo.Setup(r => r.UpsertRecordAsync(Cls, Arm, Yesterday, Term, TeacherAffiliation,
                It.IsAny<IReadOnlyList<(Guid, AttendanceStatus)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), DateTime.UtcNow));

        await CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None);

        _repo.Verify(r => r.UpsertRecordAsync(Cls, Arm, Yesterday, Term, TeacherAffiliation,
            It.IsAny<IReadOnlyList<(Guid, AttendanceStatus)>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // "No current term → blocked" is enforced at the edge by [RequiresCurrentTerm]
    // (see RequiresCurrentTermAttributeTests), so the service no longer re-checks it.

    // ---- term attribution: the register belongs to the term CONTAINING the date, ----
    // ---- not whichever term happens to be current when it is (back)entered.      ----

    [Fact]
    public async Task Submit_BackdatedIntoEarlierTerm_BooksToThatTerm()
    {
        AsOwner(); UnitExists(TeacherAffiliation); RosterHas(StudentA, StudentB);
        Guid earlierTerm = Guid.NewGuid();
        _repo.Setup(r => r.GetTermIdForDateAsync(Yesterday, It.IsAny<CancellationToken>())).ReturnsAsync(earlierTerm);
        _repo.Setup(r => r.UpsertRecordAsync(Cls, Arm, Yesterday, earlierTerm, null,
                It.IsAny<IReadOnlyList<(Guid, AttendanceStatus)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), DateTime.UtcNow));

        await CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None);

        _repo.Verify(r => r.UpsertRecordAsync(Cls, Arm, Yesterday, earlierTerm, null,
            It.IsAny<IReadOnlyList<(Guid, AttendanceStatus)>>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.GetCurrentTermIdAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_DateInHolidayGap_Throws400()
    {
        AsOwner(); UnitExists(TeacherAffiliation); RosterHas(StudentA, StudentB);
        _repo.Setup(r => r.GetTermIdForDateAsync(Yesterday, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        _repo.Setup(r => r.HasDatedTermsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    // Legacy schools may not have set term dates yet — the current term remains the fallback there.
    [Fact]
    public async Task Submit_NoDatedTerms_FallsBackToCurrentTerm()
    {
        AsOwner(); UnitExists(TeacherAffiliation); RosterHas(StudentA, StudentB);
        _repo.Setup(r => r.GetTermIdForDateAsync(Yesterday, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        _repo.Setup(r => r.HasDatedTermsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.GetCurrentTermIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Term);
        _repo.Setup(r => r.UpsertRecordAsync(Cls, Arm, Yesterday, Term, null,
                It.IsAny<IReadOnlyList<(Guid, AttendanceStatus)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), DateTime.UtcNow));

        await CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None);

        _repo.Verify(r => r.UpsertRecordAsync(Cls, Arm, Yesterday, Term, null,
            It.IsAny<IReadOnlyList<(Guid, AttendanceStatus)>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- validation ----

    [Fact]
    public async Task Submit_FutureDate_Throws400()
    {
        AsOwner();
        UnitExists(TeacherAffiliation);
        SubmitAttendanceRequest req = new SubmitAttendanceRequest
        {
            ClassId = Cls, ArmId = Arm, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Marks = ValidSubmit().Marks
        };

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_EmptyMarks_Throws400()
    {
        AsOwner();
        UnitExists(TeacherAffiliation);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(new SubmitAttendanceRequest { ClassId = Cls, ArmId = Arm, Date = Yesterday }, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_MissingStatus_Throws400()
    {
        // Status omitted → null → service rejects (an invalid status is unrepresentable with the enum).
        AsOwner();
        UnitExists(TeacherAffiliation);
        RosterHas(StudentA);
        SubmitAttendanceRequest req = new SubmitAttendanceRequest
        {
            ClassId = Cls, ArmId = Arm, Date = Yesterday,
            Marks = new List<AttendanceMarkInput> { new AttendanceMarkInput { StudentId = StudentA } }
        };

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_StudentNotInUnit_Throws400()
    {
        AsOwner();
        UnitExists(TeacherAffiliation);
        RosterHas(StudentA);                      // StudentB is not in the unit

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_DuplicateStudent_Throws400()
    {
        AsOwner();
        UnitExists(TeacherAffiliation);
        RosterHas(StudentA);
        SubmitAttendanceRequest req = new SubmitAttendanceRequest
        {
            ClassId = Cls, ArmId = Arm, Date = Yesterday,
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

    // ---- roster + units ----

    [Fact]
    public async Task ListMarkableArms_ForwardsAffiliationAndOwnerFlag()
    {
        AsClassTeacher(TeacherAffiliation);
        _repo.Setup(r => r.ListMarkableArmsAsync(TeacherAffiliation, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new MarkableArmRow { ArmId = Arm, ArmName = "JSS 1A", ClassId = Cls, ClassName = "JSS 1", Level = "junior_secondary" } });

        IReadOnlyList<MarkableArmResponse> arms = await CreateSut().ListMarkableArmsAsync(CancellationToken.None);

        Assert.Single(arms);
        Assert.Equal("JSS 1A", arms[0].ArmName);
        _repo.Verify(r => r.ListMarkableArmsAsync(TeacherAffiliation, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Roster_SetsSubmittedFromRecordExists()
    {
        AsOwner();
        UnitExists(TeacherAffiliation);
        _repo.Setup(r => r.GetRosterAsync(Cls, Arm, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RosterStudentRow { StudentId = StudentA, StudentName = "Tolu Adebayo", Status = "present" } });
        _repo.Setup(r => r.RecordExistsAsync(Cls, Arm, Yesterday, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        AttendanceRosterResponse res = await CreateSut().GetRosterAsync(Cls, Arm, Yesterday, CancellationToken.None);

        Assert.True(res.Submitted);
        Assert.Single(res.Students);
        Assert.Equal(AttendanceStatus.Present, res.Students[0].Status);
    }

    // ---- overview ----

    [Fact]
    public async Task Overview_ComputesPercentsAndTotalsFromSubmittedUnitsOnly()
    {
        AsOwner();
        DateOnly date = Yesterday;
        _repo.Setup(r => r.GetOverviewArmStatsAsync(date, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new ArmStatRow { ClassId = Cls, ArmId = Arm, ArmName = "JSS 1A", Submitted = true,  PresentCount = 18, AbsentCount = 2, LateCount = 0, TotalCount = 20 },
            new ArmStatRow { ClassId = Guid.NewGuid(), ArmId = Guid.NewGuid(), ArmName = "JSS 1B", Submitted = false, PresentCount = 0, AbsentCount = 0, LateCount = 0, TotalCount = 25 }
        });
        _repo.Setup(r => r.GetAbsentStudentsAsync(date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AbsentStudentRow { StudentName = "Ada Obi", ArmName = "JSS 1A" } });

        AttendanceOverviewResponse res = await CreateSut().GetOverviewAsync(date, CancellationToken.None);

        Assert.Equal(18, res.TotalPresent);
        Assert.Equal(2, res.TotalAbsent);
        Assert.Equal(20, res.TotalStudents);          // the unsubmitted unit contributes nothing
        Assert.Equal(90, res.OverallPresentPct);      // 18 / 20
        Assert.Equal(90, res.Arms.First(a => a.ArmName == "JSS 1A").PresentPct);
        Assert.Equal(0, res.Arms.First(a => a.ArmName == "JSS 1B").PresentPct);
        Assert.Single(res.AbsentStudents);
    }
}
