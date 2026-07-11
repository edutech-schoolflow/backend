using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Ports;
using EduTech.Students.ParentFacing;
using Moq;

namespace EduTech.Auth.Tests.Students;

/// <summary>
/// EDD-002 — "my children" resolved from the IDENTITY session (not a parent token). The identity home
/// works before any parent relationship exists: no profile → no children, no error.
/// </summary>
public class IdentityChildrenServiceTests
{
    private readonly Mock<IParentChildrenRepository> _repo = new();
    private readonly Mock<IEduTechRequestContext> _context = new();
    private readonly Mock<IStudentFeeBalanceProvider> _balances = new();

    private ParentChildrenService CreateSut() => new(_repo.Object, _context.Object, _balances.Object);

    private static readonly Guid Identity = Guid.NewGuid();

    [Fact]
    public async Task GetMyChildren_NoParentProfile_ReturnsEmpty_NoError()
    {
        _context.SetupGet(c => c.IdentityId).Returns(Identity.ToString());
        _repo.Setup(r => r.GetParentIdByIdentityAsync(Identity, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        IReadOnlyList<ParentChildResponse> children = await CreateSut().GetMyChildrenAsync(CancellationToken.None);

        Assert.Empty(children);
        _repo.Verify(r => r.GetChildrenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyChildren_ResolvesParentFromIdentity_ReturnsChildren()
    {
        Guid parentId = Guid.NewGuid();
        _context.SetupGet(c => c.IdentityId).Returns(Identity.ToString());
        _repo.Setup(r => r.GetParentIdByIdentityAsync(Identity, It.IsAny<CancellationToken>())).ReturnsAsync(parentId);
        _repo.Setup(r => r.GetChildrenAsync(parentId, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new ParentChildRow { ChildProfileId = Guid.NewGuid(), StudentName = "Ada Obi" }
        });

        IReadOnlyList<ParentChildResponse> children = await CreateSut().GetMyChildrenAsync(CancellationToken.None);

        ParentChildResponse child = Assert.Single(children);
        Assert.Equal("Ada Obi", child.StudentName);
        _repo.Verify(r => r.GetChildrenAsync(parentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // An identity-scope session carries the identity as user_id (no separate identity_id claim).
    [Fact]
    public async Task GetMyChildren_IdentitySession_UsesUserIdAsIdentity()
    {
        _context.SetupGet(c => c.IdentityId).Returns((string?)null);
        _context.SetupGet(c => c.UserId).Returns(Identity.ToString());
        _repo.Setup(r => r.GetParentIdByIdentityAsync(Identity, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        await CreateSut().GetMyChildrenAsync(CancellationToken.None);

        _repo.Verify(r => r.GetParentIdByIdentityAsync(Identity, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Add-child is the Stage-1 completion point: it provisions the parent profile, then saves the child.
    [Fact]
    public async Task UpsertMyChild_ProvisionsParentThenSaves()
    {
        Guid parentId = Guid.NewGuid();
        Guid childId = Guid.NewGuid();
        _context.SetupGet(c => c.IdentityId).Returns(Identity.ToString());
        _repo.Setup(r => r.GetOrProvisionParentIdAsync(Identity, It.IsAny<CancellationToken>())).ReturnsAsync(parentId);
        _repo.Setup(r => r.InsertChildProfileAsync(parentId, It.IsAny<ChildProfileInsert>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(childId);

        Guid result = await CreateSut().UpsertMyChildAsync(
            new UpsertChildProfileRequest { FirstName = "Ada", LastName = "Obi", DateOfBirth = new DateOnly(2015, 1, 1) },
            CancellationToken.None);

        Assert.Equal(childId, result);
        _repo.Verify(r => r.GetOrProvisionParentIdAsync(Identity, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.InsertChildProfileAsync(parentId, It.IsAny<ChildProfileInsert>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertMyChild_MissingName_Throws400()
    {
        _context.SetupGet(c => c.IdentityId).Returns(Identity.ToString());
        _repo.Setup(r => r.GetOrProvisionParentIdAsync(Identity, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().UpsertMyChildAsync(
            new UpsertChildProfileRequest { FirstName = "", LastName = "Obi", DateOfBirth = new DateOnly(2015, 1, 1) },
            CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }
}
