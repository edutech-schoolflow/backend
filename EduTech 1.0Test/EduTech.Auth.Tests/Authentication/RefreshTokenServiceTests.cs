using EduTech.Auth;
using EduTech.Auth.RefreshTokens;
using Moq;

namespace EduTech.Auth.Tests.Authentication;

/// <summary>
/// EDD-012 B2c.3c — refresh re-key mechanics. Rotation now carries the canonical (identity, context)
/// alongside the legacy actor, and the strangler theft-detection / lineage guarantees are unchanged:
/// one-time use, reuse (incl. concurrent double-refresh) nukes the family, expiry does not extend.
/// </summary>
public class RefreshTokenServiceTests
{
    private readonly Mock<IRefreshTokenRepository> _repo = new();
    private RefreshTokenService Sut() => new(_repo.Object);

    private static RefreshTokenRow Row(Guid? identityId = null, Guid? contextId = null,
        DateTime? expiresAt = null, DateTime? rotatedAt = null, DateTime? revokedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        ActorType = AuthActorTypes.Staff,
        ActorId = Guid.NewGuid(),
        IdentityId = identityId,
        ContextId = contextId,
        FamilyId = Guid.NewGuid(),
        ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1),
        RotatedAt = rotatedAt,
        RevokedAt = revokedAt
    };

    [Fact]
    public async Task Issue_StoresTheCanonicalKey()
    {
        Guid identityId = Guid.NewGuid(), contextId = Guid.NewGuid();

        await Sut().IssueAsync(AuthActorTypes.Staff, Guid.NewGuid(), identityId, contextId, "ip", "ua");

        _repo.Verify(r => r.InsertAsync(AuthActorTypes.Staff, It.IsAny<Guid>(), identityId, contextId,
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), "ip", "ua", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Rotate_PreservesIdentityAndContext_AndContinuesTheFamily()
    {
        RefreshTokenRow row = Row(identityId: Guid.NewGuid(), contextId: Guid.NewGuid());
        _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(row);

        RefreshRotationResult result = await Sut().RotateAsync("raw", "ip", "ua");

        Assert.True(result.IsSuccess);
        Assert.Equal(row.IdentityId, result.IdentityId);
        Assert.Equal(row.ContextId, result.ContextId);
        // Replacement carries the SAME canonical key, family, and expiry — rotation renews, never re-keys.
        _repo.Verify(r => r.InsertAsync(row.ActorType, row.ActorId, row.IdentityId, row.ContextId,
            It.IsAny<string>(), row.FamilyId, row.ExpiresAt, "ip", "ua", It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.MarkRotatedAsync(row.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, false)]   // already rotated — a concurrent/second use of the same token
    [InlineData(false, true)]   // already revoked
    public async Task Rotate_ReusedToken_RevokesFamily_AndIssuesNothing(bool rotated, bool revoked)
    {
        RefreshTokenRow row = Row(rotatedAt: rotated ? DateTime.UtcNow : null,
            revokedAt: revoked ? DateTime.UtcNow : null);
        _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(row);

        RefreshRotationResult result = await Sut().RotateAsync("raw", null, null);

        Assert.Equal(RefreshTokenStatus.Reused, result.Status);
        _repo.Verify(r => r.RevokeFamilyAsync(row.FamilyId, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.InsertAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rotate_Expired_Fails()
    {
        _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(expiresAt: DateTime.UtcNow.AddSeconds(-1)));

        Assert.Equal(RefreshTokenStatus.Expired, (await Sut().RotateAsync("raw", null, null)).Status);
    }

    [Fact]
    public async Task Rotate_NotFound_Fails()
    {
        _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshTokenRow?)null);

        Assert.Equal(RefreshTokenStatus.NotFound, (await Sut().RotateAsync("bad", null, null)).Status);
    }
}
