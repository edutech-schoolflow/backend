using EduTech.Fees;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using Moq;

namespace EduTech.Auth.Tests.Fees;

public class SchoolFeeServiceTests
{
    private readonly Mock<ISchoolFeeRepository> _repo = new();
    private readonly Mock<IEduTechRequestContext> _context = new();

    private static readonly Guid Term = Guid.NewGuid();
    private static readonly Guid Class = Guid.NewGuid();
    private static readonly Guid Fee = Guid.NewGuid();

    private SchoolFeeService CreateSut(bool isOwner = true)
    {
        _context.SetupGet(c => c.IsOwner).Returns(isOwner);
        _context.SetupGet(c => c.UserId).Returns(Guid.NewGuid().ToString());
        return new SchoolFeeService(_repo.Object, _context.Object);
    }

    private static CreateFeeTypeRequest Req(decimal amount = 50000, FeeCategory? cat = null, params Guid[] classes) =>
        new CreateFeeTypeRequest { Name = "Tuition", Amount = amount, TermId = Term, Category = cat, ClassIds = classes.ToList() };

    private static FeeTypeRow Row(string approval = "pending_approval", string category = "compulsory") =>
        new FeeTypeRow { Id = Fee, Name = "Tuition", Amount = 50000m, TermId = Term, Category = category,
            ApprovalStatus = approval, IsActive = true, ClassIds = Class.ToString() };

    private static UpdateFeeTypeRequest Upd() =>
        new UpdateFeeTypeRequest { Name = "Tuition", Amount = 60000m, ClassIds = new List<Guid> { Class } };

    // ---- create + categories + approval-by-creator ----

    [Fact]
    public async Task CreateFeeType_NoClasses_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().CreateFeeTypeAsync(Req(50000), CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task CreateFeeType_TermMissing_Throws404()
    {
        _repo.Setup(r => r.TermExistsAsync(Term, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().CreateFeeTypeAsync(Req(50000, null, Class), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task CreateFeeType_ByOwner_IsApproved()
    {
        _repo.Setup(r => r.TermExistsAsync(Term, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.CreateFeeTypeAsync(It.IsAny<string>(), It.IsAny<decimal>(), Term, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fee);

        FeeTypeResponse res = await CreateSut(isOwner: true).CreateFeeTypeAsync(Req(50000, FeeCategory.Optional, Class), CancellationToken.None);

        Assert.Equal(FeeApprovalStatus.Approved, res.ApprovalStatus);
        Assert.Equal(FeeCategory.Optional, res.Category);
        _repo.Verify(r => r.CreateFeeTypeAsync("Tuition", 50000m, Term, "optional", "approved", true,
            It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateFeeType_ByStaff_IsPendingApproval()
    {
        _repo.Setup(r => r.TermExistsAsync(Term, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.CreateFeeTypeAsync(It.IsAny<string>(), It.IsAny<decimal>(), Term, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fee);

        FeeTypeResponse res = await CreateSut(isOwner: false).CreateFeeTypeAsync(Req(50000, null, Class), CancellationToken.None);

        Assert.Equal(FeeApprovalStatus.PendingApproval, res.ApprovalStatus);
        Assert.Equal(FeeCategory.Compulsory, res.Category);   // default
        _repo.Verify(r => r.CreateFeeTypeAsync("Tuition", 50000m, Term, "compulsory", "pending_approval", false,
            null, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- edit lock ----

    [Fact]
    public async Task Update_Approved_Throws409_Locked()
    {
        _repo.Setup(r => r.GetFeeTypeAsync(Fee, It.IsAny<CancellationToken>())).ReturnsAsync(Row("approved"));
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().UpdateFeeTypeAsync(Fee, Upd(), CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
        _repo.Verify(r => r.UpdateFeeTypeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Pending_Updates()
    {
        _repo.Setup(r => r.GetFeeTypeAsync(Fee, It.IsAny<CancellationToken>())).ReturnsAsync(Row("pending_approval"));
        await CreateSut().UpdateFeeTypeAsync(Fee, Upd(), CancellationToken.None);
        _repo.Verify(r => r.UpdateFeeTypeAsync(Fee, "Tuition", 60000m, It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- owner approve / reject ----

    [Fact]
    public async Task Approve_ByStaff_Throws403()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut(isOwner: false).ApproveFeeTypeAsync(Fee, CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Approve_OwnerPending_Approves()
    {
        _repo.SetupSequence(r => r.GetFeeTypeAsync(Fee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("pending_approval")).ReturnsAsync(Row("approved"));
        _repo.Setup(r => r.ApproveAsync(Fee, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

        FeeTypeResponse res = await CreateSut(isOwner: true).ApproveFeeTypeAsync(Fee, CancellationToken.None);

        Assert.Equal(FeeApprovalStatus.Approved, res.ApprovalStatus);
        _repo.Verify(r => r.ApproveAsync(Fee, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_Rejected_Throws409()
    {
        _repo.Setup(r => r.GetFeeTypeAsync(Fee, It.IsAny<CancellationToken>())).ReturnsAsync(Row("rejected"));
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut(isOwner: true).ApproveFeeTypeAsync(Fee, CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);   // rejected -> approved isn't allowed
    }

    [Fact]
    public async Task Reject_OwnerPending_Rejects()
    {
        _repo.SetupSequence(r => r.GetFeeTypeAsync(Fee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("pending_approval")).ReturnsAsync(Row("rejected"));
        _repo.Setup(r => r.RejectAsync(Fee, "no budget", It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await CreateSut(isOwner: true).RejectFeeTypeAsync(Fee, new RejectFeeTypeRequest { Reason = "no budget" }, CancellationToken.None);

        _repo.Verify(r => r.RejectAsync(Fee, "no budget", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- delete / archive (integrity) ----

    [Fact]
    public async Task Delete_Unused_HardDeletes()
    {
        _repo.Setup(r => r.GetFeeTypeAsync(Fee, It.IsAny<CancellationToken>())).ReturnsAsync(Row());
        _repo.Setup(r => r.FeeTypeIsUsedAsync(Fee, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        bool archived = await CreateSut().DeleteFeeTypeAsync(Fee, CancellationToken.None);

        Assert.False(archived);
        _repo.Verify(r => r.DeleteFeeTypeAsync(Fee, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_Billed_ArchivesInstead()
    {
        _repo.Setup(r => r.GetFeeTypeAsync(Fee, It.IsAny<CancellationToken>())).ReturnsAsync(Row("approved"));
        _repo.Setup(r => r.FeeTypeIsUsedAsync(Fee, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        bool archived = await CreateSut().DeleteFeeTypeAsync(Fee, CancellationToken.None);

        Assert.True(archived);
        _repo.Verify(r => r.ArchiveFeeTypeAsync(Fee, It.IsAny<CancellationToken>()), Times.Once);
    }
}
