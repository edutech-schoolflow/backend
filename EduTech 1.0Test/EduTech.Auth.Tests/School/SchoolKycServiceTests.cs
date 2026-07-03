using EduTech.School.Kyc;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Identity;
using EduTech.Shared.Persistence;
using EduTech.Shared.Security;
using EduTech.Shared.Storage;
using Moq;

namespace EduTech.Auth.Tests.School;

/// <summary>
/// Pre-transaction guards (already submitted, missing fields/docs) and the status mapping. The full
/// transactional submit is covered by the live E2E.
/// </summary>
public class SchoolKycServiceTests
{
    private readonly Mock<IEduTechRequestContext> _context = new();
    private readonly Mock<ISchoolKycRepository> _repo = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IFieldEncryptor> _encryptor = new();
    private readonly Mock<IIdentityVerifier> _verifier = new();
    private readonly Mock<IDbConnectionFactory> _db = new();

    public SchoolKycServiceTests()
    {
        _context.Setup(c => c.SchoolId).Returns(Guid.NewGuid().ToString());
        _encryptor.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => "enc:" + s);
        _verifier.Setup(v => v.VerifyNinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityVerificationResult.Ok());
        _verifier.Setup(v => v.VerifyBvnAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityVerificationResult.Ok());
    }

    private SchoolKycService CreateSut()
    {
        return new SchoolKycService(_context.Object, _repo.Object, _storage.Object, _encryptor.Object,
            _verifier.Object, _db.Object);
    }

    private static SubmitKycRequest ValidTextRequest()
    {
        return new SubmitKycRequest
        {
            Name = "Greenfield Academy", Type = "primary", Address = "1 Road", City = "Kano", State = "Kano",
            Phone = "08012345678", Email = "info@greenfield.com",
            ProprietorFirstName = "Grace", ProprietorLastName = "Okafor",
            ProprietorNin = "11122233344", ProprietorBvn = "55566677788",
            BankName = "Access Bank", AccountNumber = "0123456789", AccountName = "Greenfield"
            // no CAC document → triggers the missing-document guard
        };
    }

    [Fact]
    public async Task Submit_AlreadyUnderReview_Throws409()
    {
        _repo.Setup(r => r.GetKycStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("under_review");

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(new SubmitKycRequest(), CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_MissingRequiredText_Throws400()
    {
        _repo.Setup(r => r.GetKycStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("not_submitted");

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(new SubmitKycRequest(), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_MissingDocument_Throws400_AndUploadsNothing()
    {
        _repo.Setup(r => r.GetKycStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("not_submitted");

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidTextRequest(), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        _storage.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_ProprietorIdentityUnverified_Throws422_AndUploadsNothing()
    {
        _repo.Setup(r => r.GetKycStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("not_submitted");
        _verifier.Setup(v => v.VerifyNinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityVerificationResult.Fail("NIN not found."));

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitAsync(ValidTextRequest(), CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);   // identity gate fires before upload/commit
        _storage.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatus_ReturnsStatusMetadataOnly()
    {
        DateTime submittedAt = DateTime.UtcNow;
        _repo.Setup(r => r.GetKycStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("under_review");
        // The submission row still holds PII/bank, but the owner response must NOT echo it back.
        _repo.Setup(r => r.GetSubmissionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KycSubmissionRow
            {
                ProprietorName = "Grace",
                BankName = "Access",
                SubmittedAt = submittedAt,
                SchoolMessage = "Looks good"
            });

        KycSubmissionResponse result = await CreateSut().GetStatusAsync(CancellationToken.None);

        Assert.Equal("under_review", result.Status);
        Assert.Equal(submittedAt, result.SubmittedAt);
        Assert.Equal("Looks good", result.SchoolMessage);
    }
}
