using EduTech.Auth.Otp;
using EduTech.Auth.Parent;
using EduTech.Auth.RefreshTokens;
using EduTech.Auth.Security;
using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using Moq;

namespace EduTech.Auth.Tests.Parent;

/// <summary>
/// Parent (Actor 3) auth — school-agnostic, phone-first. Parent registration is a single insert (no
/// transaction), so the happy path is unit-testable end to end here.
/// </summary>
public class ParentAuthServiceTests
{
    private readonly Mock<IEduTechRequestContext> _context = new();
    private readonly Mock<IParentRepository> _parents = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IOtpService> _otp = new();
    private readonly Mock<INotificationDispatcher> _sms = new();
    private readonly Mock<IAccessTokenIssuer> _access = new();
    private readonly Mock<IRefreshTokenService> _refresh = new();

    private ParentAuthService CreateSut()
    {
        return new ParentAuthService(
            _context.Object, _parents.Object, _hasher.Object, _otp.Object, _sms.Object,
            _access.Object, _refresh.Object);
    }

    [Fact]
    public async Task Register_Valid_CreatesParentNormalizesPhoneAndSendsOtp()
    {
        Guid parentId = Guid.NewGuid();
        _parents.Setup(p => p.ExistsByPhoneAsync("+2348033334444", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _hasher.Setup(h => h.Hash("password123")).Returns("hashed");
        _parents.Setup(p => p.CreateAsync("John", null, "Okafor", "+2348033334444", null, "hashed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentId);
        _otp.Setup(o => o.GenerateAsync(OtpPurpose.ParentPhoneVerification, parentId, "+2348033334444",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("123456");

        await CreateSut().RegisterAsync(
            new RegisterParentRequest { FirstName = "John", LastName = "Okafor", Phone = "08033334444", Password = "password123" },
            CancellationToken.None);

        _parents.Verify(p => p.CreateAsync("John", null, "Okafor", "+2348033334444", null, "hashed",
            It.IsAny<CancellationToken>()), Times.Once);
        _sms.Verify(s => s.SendSmsAsync("+2348033334444", It.Is<string>(m => m.Contains("123456")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_DuplicatePhone_Throws409WithGenericMessage()
    {
        _parents.Setup(p => p.ExistsByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().RegisterAsync(
            new RegisterParentRequest { FirstName = "John", LastName = "Doe", Phone = "08033334444", Password = "password123" },
            CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.PhoneTaken, ex.ErrorCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Throws401AndRecordsFailure()
    {
        ParentLoginRow parent = new()
        {
            Id = Guid.NewGuid(),
            PasswordHash = "stored-hash",
            PhoneVerified = true,
            IsActive = true
        };
        _parents.Setup(p => p.GetByPhoneForLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().LoginAsync(
            new ParentLoginRequest { Phone = "08033334444", Password = "wrong" }, null, null, CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
        _parents.Verify(p => p.SetLoginFailureAsync(parent.Id, It.IsAny<int>(), It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetPaymentPin_InvalidLength_Throws400()
    {
        _context.Setup(c => c.UserId).Returns(Guid.NewGuid().ToString());

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().SetPaymentPinAsync(
            new SetPaymentPinRequest { Pin = "12" }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        _parents.Verify(p => p.SetPaymentPinAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetPaymentPin_ValidSixDigits_HashesAndStores()
    {
        Guid parentId = Guid.NewGuid();
        _context.Setup(c => c.UserId).Returns(parentId.ToString());
        _hasher.Setup(h => h.Hash("135790")).Returns("pin-hash");

        await CreateSut().SetPaymentPinAsync(new SetPaymentPinRequest { Pin = "135790" }, CancellationToken.None);

        _parents.Verify(p => p.SetPaymentPinAsync(parentId, "pin-hash", It.IsAny<CancellationToken>()), Times.Once);
    }
}
