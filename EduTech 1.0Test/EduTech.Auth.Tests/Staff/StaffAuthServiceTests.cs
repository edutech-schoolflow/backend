using EduTech.Auth.Otp;
using EduTech.Auth.RefreshTokens;
using EduTech.Auth.Security;
using EduTech.Auth.Staff;
using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using Moq;

namespace EduTech.Auth.Tests.Staff;

/// <summary>
/// Staff (Actor 2) standalone auth — register → verify → login (identity-only token; school scope
/// comes later via /staff/schools). Single-insert registration, so the happy path is unit-testable.
/// </summary>
public class StaffAuthServiceTests
{
    private readonly Mock<IEduTechRequestContext> _context = new();
    private readonly Mock<IStaffUserRepository> _staff = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IOtpService> _otp = new();
    private readonly Mock<INotificationDispatcher> _sms = new();
    private readonly Mock<IAccessTokenIssuer> _access = new();
    private readonly Mock<IRefreshTokenService> _refresh = new();

    private StaffAuthService CreateSut()
    {
        return new StaffAuthService(
            _context.Object, _staff.Object, _hasher.Object, _otp.Object, _sms.Object,
            _access.Object, _refresh.Object);
    }

    [Fact]
    public async Task Register_Valid_CreatesStaffAndSendsOtp()
    {
        Guid staffId = Guid.NewGuid();
        _staff.Setup(s => s.ExistsByPhoneAsync("+2348055667788", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _hasher.Setup(h => h.Hash("password123")).Returns("hashed");
        _staff.Setup(s => s.CreateAsync("Amaka", null, "Teacher", "+2348055667788", null, "hashed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(staffId);
        _otp.Setup(o => o.GenerateAsync(OtpPurpose.StaffPhoneVerification, staffId, "+2348055667788",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("654321");

        await CreateSut().RegisterAsync(
            new RegisterStaffRequest { FirstName = "Amaka", LastName = "Teacher", Phone = "08055667788", Password = "password123" },
            CancellationToken.None);

        _sms.Verify(s => s.SendSmsAsync("+2348055667788", It.Is<string>(m => m.Contains("654321")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_UnverifiedPhoneButCorrectPassword_Throws403()
    {
        StaffUserLoginRow staff = new()
        {
            Id = Guid.NewGuid(),
            PasswordHash = "stored-hash",
            PhoneVerified = false,
            IsActive = true,
            KycStatus = "not_submitted"
        };
        _staff.Setup(s => s.GetByPhoneForLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staff);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().LoginAsync(
            new StaffLoginRequest { Phone = "08055667788", Password = "password123" }, null, null, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(ErrorCodes.PhoneNotVerified, ex.ErrorCode);
    }

    [Fact]
    public async Task Login_InactiveAccount_Throws403()
    {
        StaffUserLoginRow staff = new()
        {
            Id = Guid.NewGuid(),
            PasswordHash = "stored-hash",
            PhoneVerified = true,
            IsActive = false
        };
        _staff.Setup(s => s.GetByPhoneForLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staff);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().LoginAsync(
            new StaffLoginRequest { Phone = "08055667788", Password = "password123" }, null, null, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(ErrorCodes.AccountInactive, ex.ErrorCode);
    }
}
