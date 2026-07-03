using EduTech.Grades.Scores;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using Moq;

namespace EduTech.Auth.Tests.Grades;

public class GradeServiceTests
{
    private readonly Mock<IGradeRepository> _repo = new();
    private readonly Mock<IEduTechRequestContext> _context = new();

    private GradeService CreateSut() => new(_repo.Object, _context.Object);

    private static readonly Guid Arm = Guid.NewGuid();
    private static readonly Guid ClassId = Guid.NewGuid();
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly Guid TermId = Guid.NewGuid();
    private static readonly Guid TeacherAff = Guid.NewGuid();
    private static readonly Guid StudentA = Guid.NewGuid();
    private static readonly Guid StudentB = Guid.NewGuid();

    private void AsOwner()
    {
        _context.SetupGet(c => c.IsOwner).Returns(true);
        _context.SetupGet(c => c.AffiliationId).Returns((string?)null);
    }

    private void AsStaff(Guid affiliation)
    {
        _context.SetupGet(c => c.IsOwner).Returns(false);
        _context.SetupGet(c => c.AffiliationId).Returns(affiliation.ToString());
    }

    private void Arm_Is(string level, Guid? classTeacher) =>
        _repo.Setup(r => r.GetArmAsync(Arm, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArmGradingRow { Id = Arm, ArmName = "JSS 1A", ClassId = ClassId, Level = level, ClassTeacherAffiliationId = classTeacher });

    private void Subject_Is(Guid classId) =>
        _repo.Setup(r => r.GetSubjectAsync(Subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubjectInfoRow { Id = Subject, ClassId = classId, Name = "Mathematics", MaxCa = 30, MaxExam = 40 });

    private void CommonSetup()
    {
        _repo.Setup(r => r.TermExistsAsync(TermId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.GetActiveStudentIdsAsync(Arm, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { StudentA, StudentB });
        _repo.Setup(r => r.GetRecordHeaderAsync(Arm, Subject, TermId, It.IsAny<AssessmentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GradeRecordHeaderRow?)null);
        _repo.Setup(r => r.UpsertRecordAsync(Arm, Subject, TermId, It.IsAny<AssessmentType>(), It.IsAny<int>(),
                It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<(Guid, decimal)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), DateTime.UtcNow));
    }

    private static SubmitGradesRequest ValidSubmit() => new SubmitGradesRequest
    {
        ArmId = Arm, SubjectId = Subject, TermId = TermId, AssessmentType = AssessmentType.FirstCa,
        Entries = new List<GradeEntryInput>
        {
            new GradeEntryInput { StudentId = StudentA, Score = 25 },
            new GradeEntryInput { StudentId = StudentB, Score = 18 },
        }
    };

    // ---- the level-dependent authorization matrix ----

    [Fact]
    public async Task Submit_Owner_AnyArm_Succeeds()
    {
        AsOwner(); Arm_Is("primary", Guid.NewGuid()); Subject_Is(ClassId); CommonSetup();
        GradeRecordSummaryResponse res = await CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None);
        Assert.Equal(2, res.EnteredCount);
    }

    [Fact]
    public async Task Submit_Primary_ClassTeacher_Succeeds()
    {
        AsStaff(TeacherAff); Arm_Is("primary", TeacherAff); Subject_Is(ClassId); CommonSetup();
        GradeRecordSummaryResponse res = await CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None);
        Assert.Equal(GradeStatus.Draft, res.Status);
    }

    [Fact]
    public async Task Submit_Primary_NotClassTeacher_Throws403()
    {
        AsStaff(Guid.NewGuid()); Arm_Is("primary", TeacherAff); Subject_Is(ClassId); CommonSetup();
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_Secondary_SubjectTeacher_Succeeds()
    {
        AsStaff(TeacherAff); Arm_Is("junior_secondary", Guid.NewGuid()); Subject_Is(ClassId); CommonSetup();
        _repo.Setup(r => r.IsSubjectTeacherAsync(Arm, "Mathematics", TeacherAff, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        GradeRecordSummaryResponse res = await CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None);
        Assert.Equal(2, res.EnteredCount);
    }

    [Fact]
    public async Task Submit_Secondary_NotSubjectTeacher_Throws403()
    {
        AsStaff(TeacherAff); Arm_Is("junior_secondary", TeacherAff); Subject_Is(ClassId); CommonSetup();
        _repo.Setup(r => r.IsSubjectTeacherAsync(Arm, "Mathematics", TeacherAff, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_SubjectNotInClass_Throws400()
    {
        AsOwner(); Arm_Is("primary", Guid.NewGuid()); Subject_Is(Guid.NewGuid()); CommonSetup();   // subject's classId != arm's
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    // ---- entry validation ----

    [Fact]
    public async Task Submit_ScoreAboveMax_Throws400()
    {
        AsOwner(); Arm_Is("primary", Guid.NewGuid()); Subject_Is(ClassId); CommonSetup();
        SubmitGradesRequest req = ValidSubmit();
        req.Entries[0] = new GradeEntryInput { StudentId = StudentA, Score = 31 };   // max_ca = 30
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_StudentNotInArm_Throws400()
    {
        AsOwner(); Arm_Is("primary", Guid.NewGuid()); Subject_Is(ClassId);
        _repo.Setup(r => r.TermExistsAsync(TermId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.GetActiveStudentIdsAsync(Arm, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { StudentA }); // B not in arm
        _repo.Setup(r => r.GetRecordHeaderAsync(Arm, Subject, TermId, It.IsAny<AssessmentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GradeRecordHeaderRow?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_NullScores_AreSkipped()
    {
        AsOwner(); Arm_Is("primary", Guid.NewGuid()); Subject_Is(ClassId); CommonSetup();
        SubmitGradesRequest req = new SubmitGradesRequest
        {
            ArmId = Arm, SubjectId = Subject, TermId = TermId, AssessmentType = AssessmentType.FirstCa,
            Entries = new List<GradeEntryInput>
            {
                new GradeEntryInput { StudentId = StudentA, Score = 20 },
                new GradeEntryInput { StudentId = StudentB, Score = null },   // not entered
            }
        };

        GradeRecordSummaryResponse res = await CreateSut().SubmitAsync(req, CancellationToken.None);

        Assert.Equal(1, res.EnteredCount);
        _repo.Verify(r => r.UpsertRecordAsync(Arm, Subject, TermId, AssessmentType.FirstCa, 30, It.IsAny<Guid?>(),
            It.Is<IReadOnlyList<(Guid StudentId, decimal Score)>>(l => l.Count == 1 && l[0].StudentId == StudentA),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_Published_Throws409()
    {
        AsOwner(); Arm_Is("primary", Guid.NewGuid()); Subject_Is(ClassId);
        _repo.Setup(r => r.TermExistsAsync(TermId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.GetRecordHeaderAsync(Arm, Subject, TermId, It.IsAny<AssessmentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GradeRecordHeaderRow { Id = Guid.NewGuid(), MaxScore = 30, Status = "published" });

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidSubmit(), CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_MissingAssessment_Throws400()
    {
        AsOwner();
        SubmitGradesRequest req = ValidSubmit();
        req = new SubmitGradesRequest { ArmId = Arm, SubjectId = Subject, TermId = TermId, AssessmentType = null, Entries = req.Entries };
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    // ---- publish lifecycle ----

    [Fact]
    public async Task Publish_Draft_Publishes()
    {
        Guid rec = Guid.NewGuid();
        _repo.Setup(r => r.GetRecordStatusAsync(rec, It.IsAny<CancellationToken>())).ReturnsAsync("draft");
        _repo.Setup(r => r.PublishRecordIfDraftAsync(rec, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await CreateSut().PublishAsync(rec, CancellationToken.None);

        _repo.Verify(r => r.PublishRecordIfDraftAsync(rec, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_AlreadyPublished_IsNoOp()
    {
        Guid rec = Guid.NewGuid();
        _repo.Setup(r => r.GetRecordStatusAsync(rec, It.IsAny<CancellationToken>())).ReturnsAsync("published");

        await CreateSut().PublishAsync(rec, CancellationToken.None);

        _repo.Verify(r => r.PublishRecordIfDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_NotFound_Throws404()
    {
        _repo.Setup(r => r.GetRecordStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().PublishAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }
}
