using EduTech.Shared.Audit;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;
using EduTech.Students.Students;
using EduTech.Students.Students.Commands;
using Moq;

namespace EduTech.Auth.Tests.Students;

public class StudentPromotionTests
{
    private readonly Mock<IStudentRepository> _repo = new();
    private readonly Mock<IDomainEventPublisher> _events = new();
    private readonly Mock<IAuditLogRepository> _audit = new();

    private StudentService CreateSut() =>
        new(_repo.Object, new StudentCommandInvoker(_events.Object), _events.Object, _audit.Object);

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
}
