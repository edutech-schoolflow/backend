using EduTech.School.Kyc;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
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
    private readonly Mock<IDbConnectionFactory> _db = new();

    public SchoolKycServiceTests()
    {
        _context.Setup(c => c.SchoolId).Returns(Guid.NewGuid().ToString());
        _encryptor.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => "enc:" + s);
    }

    private SchoolKycService CreateSut()
    {
        return new SchoolKycService(_context.Object, _repo.Object, _storage.Object, _encryptor.Object, _db.Object);
    }

    private static SubmitKycRequest ValidTextRequest()
    {
        return new SubmitKycRequest
        {
            Name = "Greenfield Academy", Type = "primary", Address = "1 Road", City = "Kano", State = "Kano",
            Phone = "08012345678", Email = "info@greenfield.com",
            ProprietorName = "Grace Okafor", ProprietorIdType = "national_id", ProprietorIdNumber = "12345678901",
            ProprietorPhone = "08012345678", ProprietorEmail = "grace@greenfield.com",
            ProprietorNin = "11122233344", ProprietorBvn = "55566677788",
            BankName = "Access Bank", AccountNumber = "0123456789", AccountName = "Greenfield", AccountType = "savings"
            // no documents → triggers the missing-document guard
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
    public async Task GetStatus_MapsSubmissionAndDocuments()
    {
        _repo.Setup(r => r.GetKycStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("under_review");
        _repo.Setup(r => r.GetSubmissionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KycSubmissionRow { ProprietorName = "Grace", BankName = "Access", AccountType = "savings" });
        _repo.Setup(r => r.GetDocumentsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KycDocumentRow>
            {
                new KycDocumentRow { Type = "registration_cert", Url = "/uploads/x", Status = "pending" }
            });

        KycSubmissionResponse result = await CreateSut().GetStatusAsync(CancellationToken.None);

        Assert.Equal("under_review", result.Status);
        Assert.Equal("Grace", result.ProprietorName);
        Assert.Single(result.Documents);
        Assert.Equal("registration_cert", result.Documents[0].Type);
    }
}
