using EduTech.Shared.Identity;
using EduTech.Compliance.Nin;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Security;
using Moq;

namespace EduTech.Auth.Tests.Compliance;

/// <summary>Staff/parent NIN compliance: validation, verification, encryption, and the derived record.</summary>
public class ComplianceServiceTests
{
    private readonly Mock<IEduTechRequestContext> _context = new();
    private readonly Mock<IComplianceRepository> _repo = new();
    private readonly Mock<IIdentityVerifier> _verifier = new();
    private readonly Mock<IFieldEncryptor> _encryptor = new();

    public ComplianceServiceTests()
    {
        _context.Setup(c => c.UserType).Returns(UserTypes.Staff);
        _context.Setup(c => c.UserId).Returns(Guid.NewGuid().ToString());
        _encryptor.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => "enc:" + s);
        _repo.Setup(r => r.GetFullNameAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Amaka Teacher");
    }

    private ComplianceService CreateSut()
    {
        return new ComplianceService(_context.Object, _repo.Object, _verifier.Object, _encryptor.Object);
    }

    [Fact]
    public async Task SubmitNin_Invalid_Throws400_AndDoesNotVerify()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitNinAsync(new SubmitNinRequest { Nin = "123" }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        _verifier.Verify(v => v.VerifyNinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitNin_VerificationFails_Throws422_AndStoresNothing()
    {
        _verifier.Setup(v => v.VerifyNinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityVerificationResult.Fail("NIN not found."));

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitNinAsync(new SubmitNinRequest { Nin = "11122233344" }, CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);
        _repo.Verify(r => r.SetNinAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitNin_Verified_EncryptsAndStoresVerified()
    {
        _verifier.Setup(v => v.VerifyNinAsync("11122233344", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityVerificationResult.Ok());
        _repo.Setup(r => r.GetAsync(UserTypes.Staff, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceStateRow { KycStatus = "verified", HasNin = true });

        ComplianceRecordResponse record =
            await CreateSut().SubmitNinAsync(new SubmitNinRequest { Nin = "11122233344" }, CancellationToken.None);

        _repo.Verify(r => r.SetNinAsync(UserTypes.Staff, It.IsAny<Guid>(), "enc:11122233344", "verified",
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("teacher", record.ActorType);   // staff surfaces as "teacher" in the frontend model
        Assert.Equal("verified", record.OverallStatus);
        Assert.Equal("verified", record.Steps[0].Status);
    }

    [Fact]
    public async Task GetRecord_NotSubmitted_MapsToNotStarted()
    {
        _repo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceStateRow?)null);

        ComplianceRecordResponse record = await CreateSut().GetRecordAsync(CancellationToken.None);

        Assert.Equal("incomplete", record.OverallStatus);
        Assert.Equal("not_started", record.Steps[0].Status);
    }

    [Fact]
    public async Task NonComplianceActor_Throws403()
    {
        _context.Setup(c => c.UserType).Returns(UserTypes.School);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().GetRecordAsync(CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
    }
}
