using EduTech.Grades.ReportCards;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using Moq;

namespace EduTech.Auth.Tests.Grades;

public class ReportCardServiceTests
{
    private readonly Mock<IReportCardRepository> _repo = new();
    private readonly Mock<IGradingScaleService> _scale = new();
    private readonly Mock<INotificationDispatcher> _notifications = new();

    private ReportCardService CreateSut() => new(_repo.Object, _scale.Object, _notifications.Object);

    private static readonly Guid Student = Guid.NewGuid();
    private static readonly Guid Arm = Guid.NewGuid();
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly Guid TermId = Guid.NewGuid();

    public ReportCardServiceTests()
    {
        _scale.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(GradingScale.Defaults);
        _notifications.Setup(n => n.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void StudentExists() =>
        _repo.Setup(r => r.GetStudentAsync(Student, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StudentReportInfoRow { Id = Student, StudentName = "Ada Obi", ClassArmId = Arm, ClassName = "JSS 1", ArmName = "JSS 1A" });

    private void TermExists() =>
        _repo.Setup(r => r.GetTermAsync(TermId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TermInfoRow { Name = "first", AcademicYear = "2025/2026" });

    [Fact]
    public async Task GetReport_PivotsScores_ComputesTotalAndGrade()
    {
        StudentExists(); TermExists();
        _repo.Setup(r => r.GetSubjectScoresAsync(Student, Arm, TermId, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new SubjectScoreRow { SubjectId = Subject, SubjectName = "Mathematics", AssessmentType = "first_ca", Score = 25 },
            new SubjectScoreRow { SubjectId = Subject, SubjectName = "Mathematics", AssessmentType = "second_ca", Score = 20 },
            new SubjectScoreRow { SubjectId = Subject, SubjectName = "Mathematics", AssessmentType = "exam", Score = 35 },
        });
        _repo.Setup(r => r.GetAttendanceSummaryAsync(Student, TermId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceSummaryRow { Present = 40, Absent = 2, Late = 1, Total = 43 });
        _repo.Setup(r => r.GetMetaAsync(Student, TermId, It.IsAny<CancellationToken>())).ReturnsAsync((ReportMetaRow?)null);

        ReportCardResponse report = await CreateSut().GetReportAsync(Student, TermId, CancellationToken.None);

        SubjectGradeResponse maths = Assert.Single(report.Grades);
        Assert.Equal(80m, maths.Total);          // 25 + 20 + 35
        Assert.Equal("A", maths.Grade);          // 70-100
        Assert.Equal(80m, report.OverallAverage);
        Assert.Equal(40, report.PresentDays);
        Assert.Equal(Term.First, report.Term);
        Assert.Equal(GradeStatus.Draft, report.Status);
    }

    [Fact]
    public async Task GetReport_StudentNotFound_Throws404()
    {
        _repo.Setup(r => r.GetStudentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((StudentReportInfoRow?)null);
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().GetReportAsync(Student, TermId, CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task SaveMeta_Published_Throws409()
    {
        StudentExists(); TermExists();
        _repo.Setup(r => r.GetStatusAsync(Student, TermId, It.IsAny<CancellationToken>())).ReturnsAsync("published");
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SaveMetaAsync(Student, TermId, new SaveReportMetaRequest(), CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task SaveMeta_BadBehavioralScore_Throws400()
    {
        StudentExists(); TermExists();
        _repo.Setup(r => r.GetStatusAsync(Student, TermId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        SaveReportMetaRequest req = new SaveReportMetaRequest
        {
            BehavioralRatings = new() { new BehavioralRatingDto { Trait = BehavioralTrait.Punctuality, Score = 9 } }
        };
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SaveMetaAsync(Student, TermId, req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task SaveMeta_Valid_Upserts()
    {
        StudentExists(); TermExists();
        _repo.Setup(r => r.GetStatusAsync(Student, TermId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        SaveReportMetaRequest req = new SaveReportMetaRequest
        {
            TeacherComment = " Good term ",
            BehavioralRatings = new() { new BehavioralRatingDto { Trait = BehavioralTrait.Neatness, Score = 4 } }
        };

        await CreateSut().SaveMetaAsync(Student, TermId, req, CancellationToken.None);

        _repo.Verify(r => r.UpsertMetaAsync(Student, TermId, Arm, "Good term", null, null,
            It.Is<IReadOnlyList<(BehavioralTrait, int)>>(b => b.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishStudent_Draft_PublishesAndNotifiesGuardians()
    {
        StudentExists(); TermExists();
        _repo.Setup(r => r.GetStatusAsync(Student, TermId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        _repo.Setup(r => r.PublishStudentAsync(Student, TermId, Arm, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        _repo.Setup(r => r.GetNotifyTargetsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new NotifyTargetRow { StudentName = "Ada Obi", Phone = "+2348030000001" } });

        await CreateSut().PublishStudentAsync(Student, TermId, CancellationToken.None);

        _notifications.Verify(n => n.SendSmsAsync("+2348030000001", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishStudent_AlreadyPublished_IsNoOp()
    {
        StudentExists(); TermExists();
        _repo.Setup(r => r.GetStatusAsync(Student, TermId, It.IsAny<CancellationToken>())).ReturnsAsync("published");

        await CreateSut().PublishStudentAsync(Student, TermId, CancellationToken.None);

        _repo.Verify(r => r.PublishStudentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishArm_NotifiesEachPublishedGuardian()
    {
        TermExists();
        Guid s2 = Guid.NewGuid();
        _repo.Setup(r => r.PublishArmAsync(Arm, TermId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Student, s2 });
        _repo.Setup(r => r.GetNotifyTargetsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new NotifyTargetRow { StudentName = "Ada Obi", Phone = "+2348030000001" },
                new NotifyTargetRow { StudentName = "Tolu Ade", Phone = "+2348030000002" },
            });

        int count = await CreateSut().PublishArmAsync(new PublishArmReportsRequest { ArmId = Arm, TermId = TermId }, CancellationToken.None);

        Assert.Equal(2, count);
        _notifications.Verify(n => n.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
