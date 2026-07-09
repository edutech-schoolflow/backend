using EduTech.Shared.Audit;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;
using EduTech.Students.Students;
using EduTech.Students.Students.Commands;
using EduTech.Identity;
using Moq;

namespace EduTech.Auth.Tests.Students;

public class StudentPromotionTests
{
    private readonly Mock<IStudentRepository> _repo = new();
    private readonly Mock<IDomainEventPublisher> _events = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<EduTech.Shared.Context.IEduTechRequestContext> _context = new();
    private readonly Mock<IIdentityDirectory> _directory = new();

    private StudentService CreateSut() =>
        new(_repo.Object, new StudentCommandInvoker(_events.Object), _events.Object, _audit.Object, _context.Object, _directory.Object);

    private static readonly Guid TargetYear = Guid.NewGuid();
    private static readonly Guid TargetClass = Guid.NewGuid();
    private static readonly Guid Student = Guid.NewGuid();

    private void ForwardSessionExists()
    {
        _repo.Setup(r => r.YearExistsAsync(TargetYear, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.IsSessionForwardAsync(TargetYear, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.ClassExistsAsync(TargetClass, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private static PromoteStudentsRequest Request(params PromotionItem[] items) => new PromoteStudentsRequest
    {
        TargetAcademicYearId = TargetYear,
        Promotions = items.ToList()
    };

    private static PromotionItem Promote(Guid studentId) => new PromotionItem
    {
        StudentId = studentId, Action = PromotionAction.Promote, TargetClassId = TargetClass
    };

    [Fact]
    public async Task Promote_ActiveStudent_Succeeds()
    {
        ForwardSessionExists();
        _repo.Setup(r => r.GetStatusAsync(Student, It.IsAny<CancellationToken>())).ReturnsAsync("active");

        PromotionResultResponse res = await CreateSut().PromoteAsync(Request(Promote(Student)), CancellationToken.None);

        Assert.Equal(1, res.Promoted);
    }

    // The same student twice in one batch would close their enrollment twice / double-move them.
    [Fact]
    public async Task Promote_DuplicateStudentInBatch_Throws400()
    {
        ForwardSessionExists();
        _repo.Setup(r => r.GetStatusAsync(Student, It.IsAny<CancellationToken>())).ReturnsAsync("active");

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().PromoteAsync(Request(Promote(Student), Promote(Student)), CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    // Only ACTIVE students move between sessions — a withdrawn or graduated student has no open
    // enrollment to close, so "promoting" them would corrupt their history.
    [Theory]
    [InlineData("withdrawn")]
    [InlineData("graduated")]
    public async Task Promote_NonActiveStudent_Throws409(string status)
    {
        ForwardSessionExists();
        _repo.Setup(r => r.GetStatusAsync(Student, It.IsAny<CancellationToken>())).ReturnsAsync(status);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().PromoteAsync(Request(Promote(Student)), CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Promote_UnknownStudent_Throws404()
    {
        ForwardSessionExists();
        _repo.Setup(r => r.GetStatusAsync(Student, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().PromoteAsync(Request(Promote(Student)), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    // ── ladder-derived targets (no explicit TargetClassId) ─────────────────────────

    [Fact]
    public async Task Promote_NoTarget_DerivesNextGradeFromLadder()
    {
        Guid jss3Class = Guid.NewGuid();
        ForwardSessionExists();
        _repo.Setup(r => r.GetStatusAsync(Student, It.IsAny<CancellationToken>())).ReturnsAsync("active");
        _repo.Setup(r => r.GetCurrentClassAsync(Student, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentClassRow { ClassId = Guid.NewGuid(), ClassName = "JSS 2" });
        _repo.Setup(r => r.FindClassIdByNameAsync("JSS 3", It.IsAny<CancellationToken>())).ReturnsAsync(jss3Class);
        _repo.Setup(r => r.ClassExistsAsync(jss3Class, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        PromotionResultResponse res = await CreateSut().PromoteAsync(Request(new PromotionItem
        {
            StudentId = Student, Action = PromotionAction.Promote // no TargetClassId
        }), CancellationToken.None);

        Assert.Equal(1, res.Promoted);
        _repo.Verify(r => r.PromoteStudentsAsync(TargetYear,
            It.Is<IReadOnlyList<PromotionCommand>>(c => c[0].TargetClassId == jss3Class),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Promote_NoTarget_FromSSS3_AutoGraduates()
    {
        ForwardSessionExists();
        _repo.Setup(r => r.GetStatusAsync(Student, It.IsAny<CancellationToken>())).ReturnsAsync("active");
        _repo.Setup(r => r.GetCurrentClassAsync(Student, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentClassRow { ClassId = Guid.NewGuid(), ClassName = "SSS 3" });

        PromotionResultResponse res = await CreateSut().PromoteAsync(Request(new PromotionItem
        {
            StudentId = Student, Action = PromotionAction.Promote
        }), CancellationToken.None);

        Assert.Equal(1, res.Graduated);
        Assert.Equal(0, res.Promoted);
    }

    [Fact]
    public async Task Promote_NoTarget_SchoolLacksNextClass_Throws400()
    {
        ForwardSessionExists();
        _repo.Setup(r => r.GetStatusAsync(Student, It.IsAny<CancellationToken>())).ReturnsAsync("active");
        _repo.Setup(r => r.GetCurrentClassAsync(Student, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentClassRow { ClassId = Guid.NewGuid(), ClassName = "Primary 6" });
        _repo.Setup(r => r.FindClassIdByNameAsync("JSS 1", It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().PromoteAsync(
            Request(new PromotionItem { StudentId = Student, Action = PromotionAction.Promote }),
            CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Repeat_NoTarget_StaysInCurrentClass()
    {
        Guid currentClass = Guid.NewGuid();
        ForwardSessionExists();
        _repo.Setup(r => r.GetStatusAsync(Student, It.IsAny<CancellationToken>())).ReturnsAsync("active");
        _repo.Setup(r => r.GetCurrentClassAsync(Student, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentClassRow { ClassId = currentClass, ClassName = "Primary 3" });
        _repo.Setup(r => r.ClassExistsAsync(currentClass, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        PromotionResultResponse res = await CreateSut().PromoteAsync(Request(new PromotionItem
        {
            StudentId = Student, Action = PromotionAction.Repeat
        }), CancellationToken.None);

        Assert.Equal(1, res.Repeated);
        _repo.Verify(r => r.PromoteStudentsAsync(TargetYear,
            It.Is<IReadOnlyList<PromotionCommand>>(c => c[0].TargetClassId == currentClass),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
