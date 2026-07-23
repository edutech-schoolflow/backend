using EduTech.Auth.PlatformAdmin;
using EduTech.Auth.RefreshTokens;
using EduTech.Auth.Security;
using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using Moq;

namespace EduTech.Auth.Tests.PlatformAdmin;

/// <summary>
/// Platform Admin (Actor 4) — email + password (no phone). Seeding is one-shot (only while no admins
/// exist); login is the standard credential/lockout tree.
/// </summary>
public class PlatformAdminAuthServiceTests
{
    private readonly Mock<IPlatformAdminRepository> _admins = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IAccessTokenIssuer> _access = new();
    private readonly Mock<IRefreshTokenService> _refresh = new();

    private PlatformAdminAuthService CreateSut()
    {
        return new PlatformAdminAuthService(
            _admins.Object, _hasher.Object, _access.Object, _refresh.Object);
    }

    private static PlatformAdminLoginRow Admin()
    {
        return new PlatformAdminLoginRow
        {
            Id = Guid.NewGuid(),
            Email = "ops@schoolflow.com",
            PasswordHash = "stored-hash",
            Role = PlatformAdminRoles.SuperAdmin,
            IsActive = true
        };
    }

    [Fact]
    public async Task Seed_WhenAdminsAlreadyExist_Throws409()
    {
        _admins.Setup(a => a.ExistsAnyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().SeedSuperAdminAsync(
            new SeedAdminRequest { FirstName = "Ops", LastName = "Admin", Email = "ops@schoolflow.com", Password = "adminpass1" },
            CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        _admins.Verify(a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_WrongPassword_Throws401AndRecordsFailure()
    {
        PlatformAdminLoginRow admin = Admin();
        _admins.Setup(a => a.GetByEmailForLoginAsync("ops@schoolflow.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(admin);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().LoginAsync(
            new AdminLoginRequest { Email = "ops@schoolflow.com", Password = "wrong" }, null, null, CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
        _admins.Verify(a => a.SetLoginFailureAsync(admin.Id, It.IsAny<int>(), It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_Valid_ReturnsTokenPair()
    {
        PlatformAdminLoginRow admin = Admin();
        // Email is normalized (trimmed + lowercased) before lookup.
        _admins.Setup(a => a.GetByEmailForLoginAsync("ops@schoolflow.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(admin);
        _hasher.Setup(h => h.Verify("adminpass1", "stored-hash")).Returns(true);
        _access.Setup(a => a.IssuePlatformAdmin(admin.Id, PlatformAdminRoles.SuperAdmin, admin.Email))
            .Returns(new AccessToken { Token = "access-jwt", ExpiresAt = DateTime.UtcNow.AddMinutes(15) });
        _refresh.Setup(r => r.IssueAsync(AuthActorTypes.PlatformAdmin, admin.Id, It.IsAny<Guid?>(), It.IsAny<Guid?>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenIssue
            {
                Token = "refresh-token",
                FamilyId = Guid.NewGuid(),
                ExpiresAt = DateTime.UtcNow.AddHours(8)
            });

        AdminTokensResult result = await CreateSut().LoginAsync(
            new AdminLoginRequest { Email = "  OPS@SchoolFlow.com  ", Password = "adminpass1" },
            null, null, CancellationToken.None);

        Assert.Equal("access-jwt", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
    }
}
