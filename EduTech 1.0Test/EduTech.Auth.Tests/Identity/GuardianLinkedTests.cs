using EduTech.Auth.Parent;
using EduTech.Auth.Unified;
using EduTech.Identity;
using EduTech.Shared.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EduTech.Auth.Tests.Identity;

/// <summary>
/// EDD-002 V2 remediation: admitting a student with a guardian phone makes the IDENTITY context
/// ensure the person (pending identity + parent link + school membership) — Academics never does.
/// </summary>
public class GuardianLinkedTests
{
    private readonly Mock<IIdentityRepository> _identities = new();
    private readonly Mock<IAuthContextRepository> _contexts = new();
    private readonly Mock<EduTech.Membership.IMembershipRepository> _memberships = new();
    private readonly Mock<IParentRepository> _parents = new();

    private EnsureIdentityOnGuardianLinked CreateSut() =>
        new(_identities.Object, _contexts.Object, _memberships.Object, _parents.Object,
            NullLogger<EnsureIdentityOnGuardianLinked>.Instance);

    private const string Phone = "+2348030000009";

    [Fact]
    public async Task Handle_EnsuresIdentityLinksParentAndRecordsMembership()
    {
        Guid schoolId = Guid.NewGuid();
        Guid identityId = Guid.NewGuid();
        Guid parentId = Guid.NewGuid();
        _identities.Setup(i => i.EnsurePendingAsync("Bola", "Ade", Phone, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identityId);
        _parents.Setup(p => p.GetIdByPhoneAsync(Phone, It.IsAny<CancellationToken>())).ReturnsAsync(parentId);

        await CreateSut().HandleAsync(new GuardianLinkedEvent(schoolId, Phone, "Bola", "Ade"), CancellationToken.None);

        _contexts.Verify(c => c.LinkParentAsync(parentId, identityId, It.IsAny<CancellationToken>()), Times.Once);
        _contexts.Verify(c => c.EnsureParentMembershipAsync(identityId, schoolId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoNames_FallsBackToGuardianPlaceholder()
    {
        Guid schoolId = Guid.NewGuid();
        _identities.Setup(i => i.EnsurePendingAsync("Guardian", "Guardian", Phone, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _parents.Setup(p => p.GetIdByPhoneAsync(Phone, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        await CreateSut().HandleAsync(new GuardianLinkedEvent(schoolId, Phone, null, null), CancellationToken.None);

        _identities.Verify(i => i.EnsurePendingAsync("Guardian", "Guardian", Phone, It.IsAny<CancellationToken>()),
            Times.Once);
        _contexts.Verify(c => c.LinkParentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
