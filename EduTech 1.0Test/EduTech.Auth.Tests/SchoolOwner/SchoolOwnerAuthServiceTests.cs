using EduTech.Auth.Otp;
using EduTech.Auth.RefreshTokens;
using EduTech.Auth.Security;
using EduTech.Auth.SchoolOwner;
using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using EduTech.Shared.Persistence;
using Moq;

namespace EduTech.Auth.Tests.SchoolOwner;

/// <summary>
/// School Owner (Actor 1) auth. The registration happy-path is transactional (school shell + owner)
/// so it's covered by integration tests; here we unit-test the guards that fire BEFORE the
/// transaction and the full login decision tree.
/// </summary>
public class SchoolOwnerAuthServiceTests
{
    private readonly Mock<IEduTechRequestContext> _context = new();
    private readonly Mock<IDbConnectionFactory> _db = new();
    private readonly Mock<ISchoolRepository> _schools = new();
    private readonly Mock<ISchoolOwnerRepository> _owners = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IOtpService> _otp = new();
    private readonly Mock<INotificationDispatcher> _sms = new();
    private readonly Mock<IAccessTokenIssuer> _access = new();
    private readonly Mock<IRefreshTokenService> _refresh = new();

    private SchoolOwnerAuthService CreateSut()
    {
        return new SchoolOwnerAuthService(
            _context.Object, _db.Object, _schools.Object, _owners.Object, _hasher.Object,
            _otp.Object, _sms.Object, _access.Object, _refresh.Object);
    }

    private static SchoolOwnerLoginRow Owner(bool verified = true, bool active = true,
        DateTime? lockedUntil = null)
    {
        return new SchoolOwnerLoginRow
        {
            Id = Guid.NewGuid(),
            SchoolId = Guid.NewGuid(),
            PasswordHash = "stored-hash",
            PhoneVerified = verified,
            IsActive = active,
            FailedLoginCount = 0,
            LockedUntil = lockedUntil
        };
    }

    // ── Login ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenPairAndClearsFailures()
    {
        SchoolOwnerLoginRow owner = Owner();
        _owners.Setup(o => o.GetByPhoneForLoginAsync("+2348012345678", It.IsAny<CancellationToken>()))
            .ReturnsAsync(owner);
        _hasher.Setup(h => h.Verify("password123", "stored-hash")).Returns(true);
        _schools.Setup(s => s.GetStatusAsync(owner.SchoolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchoolStatusRow { Status = "pending_kyc", KycStatus = "not_submitted" });
        _access.Setup(a => a.IssueSchoolOwner(owner.Id, owner.SchoolId, It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new AccessToken { Token = "access-jwt", ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        _refresh.Setup(r => r.IssueAsync(AuthActorTypes.SchoolOwner, owner.Id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenIssue
            {
                Token = "refresh-token",
                FamilyId = Guid.NewGuid(),
                ExpiresAt = DateTime.UtcNow.AddHours(12)
            });

        LoginResult result = await CreateSut().LoginAsync(
            new LoginRequest { Phone = "08012345678", Password = "password123" }, null, null, CancellationToken.None);

        Assert.Equal("access-jwt", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
        _owners.Verify(o => o.ClearLoginFailuresAsync(owner.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_WrongPassword_Throws401AndRecordsFailure()
    {
        SchoolOwnerLoginRow owner = Owner();
        _owners.Setup(o => o.GetByPhoneForLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(owner);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().LoginAsync(
            new LoginRequest { Phone = "08012345678", Password = "wrong" }, null, null, CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
        _owners.Verify(o => o.SetLoginFailureAsync(owner.Id, It.IsAny<int>(), It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_UnverifiedPhoneButCorrectPassword_Throws403PhoneNotVerified()
    {
        _owners.Setup(o => o.GetByPhoneForLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Owner(verified: false));
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().LoginAsync(
            new LoginRequest { Phone = "08012345678", Password = "password123" }, null, null, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(ErrorCodes.PhoneNotVerified, ex.ErrorCode);
    }

    [Fact]
    public async Task Login_LockedAccount_Throws429BeforeCheckingPassword()
    {
        _owners.Setup(o => o.GetByPhoneForLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Owner(lockedUntil: DateTime.UtcNow.AddMinutes(10)));

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().LoginAsync(
            new LoginRequest { Phone = "08012345678", Password = "password123" }, null, null, CancellationToken.None));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(ErrorCodes.AccountLocked, ex.ErrorCode);
        _hasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Login_UnknownPhone_Throws401Uniformly()
    {
        _owners.Setup(o => o.GetByPhoneForLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchoolOwnerLoginRow?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().LoginAsync(
            new LoginRequest { Phone = "08012345678", Password = "password123" }, null, null, CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
    }

    // ── Register guards (pre-transaction) ──────────────────────────────────────

    [Fact]
    public async Task Register_ShortPassword_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().RegisterAsync(
            new RegisterSchoolOwnerRequest { FirstName = "Jane", LastName = "Doe", Phone = "08012345678", Password = "short" },
            CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
    }

    [Fact]
    public async Task Register_InvalidPhone_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().RegisterAsync(
            new RegisterSchoolOwnerRequest { FirstName = "Jane", LastName = "Doe", Phone = "12345", Password = "password123" },
            CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicatePhone_Throws409WithGenericMessage()
    {
        _owners.Setup(o => o.ExistsByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().RegisterAsync(
            new RegisterSchoolOwnerRequest { FirstName = "Jane", LastName = "Doe", Phone = "08012345678", Password = "password123" },
            CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.PhoneTaken, ex.ErrorCode);
        // Anti-enumeration: the client never learns the phone is the reason.
        Assert.DoesNotContain("phone", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
