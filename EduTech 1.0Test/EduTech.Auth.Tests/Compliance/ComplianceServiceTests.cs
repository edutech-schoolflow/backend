using EduTech.Compliance.Nin;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Identity;
using EduTech.Shared.Security;
using Moq;

namespace EduTech.Auth.Tests.Compliance;

/// <summary>
/// EDD-017 Slice 2 — Identity NIN verification workflow: validation, provider check, encryption onto
/// Identity, one attempt record per submission, the state transitions, the events, and the Slice-2 legacy
/// kyc_status sync (which keeps Auth unchanged). Keyed on IdentityId, not the legacy actor id.
/// </summary>
public class ComplianceServiceTests
{
    private static readonly Guid IdentityId = Guid.NewGuid();

    private readonly Mock<IEduTechRequestContext> _context = new();
    private readonly Mock<IComplianceRepository> _repo = new();
    private readonly Mock<IIdentityVerifier> _verifier = new();
    private readonly Mock<IFieldEncryptor> _encryptor = new();
    private readonly Mock<IDomainEventPublisher> _events = new();

    public ComplianceServiceTests()
    {
        _context.Setup(c => c.UserType).Returns(UserTypes.Staff);
        _context.Setup(c => c.IdentityId).Returns(IdentityId.ToString());
        _verifier.Setup(v => v.ProviderName).Returns("stub");
        _encryptor.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => "enc:" + s);
        _repo.Setup(r => r.GetIdentityNameAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Amaka Teacher");
    }

    private ComplianceService CreateSut() =>
        new ComplianceService(_context.Object, _repo.Object, _verifier.Object, _encryptor.Object, _events.Object);

    private void VerifierReturns(IdentityVerificationResult result) =>
        _verifier.Setup(v => v.VerifyNinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void Published<TEvent>(Times times) where TEvent : IDomainEvent =>
        _events.Verify(e => e.PublishAsync(It.IsAny<TEvent>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task SubmitNin_Invalid_Throws400_DoesNotVerifyOrRecordOrEmit()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitNinAsync(new SubmitNinRequest { Nin = "123" }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        _verifier.Verify(v => v.VerifyNinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.RecordSuccessAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.RecordFailureAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Published<IdentityVerificationSubmittedEvent>(Times.Never());
    }

    [Fact]
    public async Task SubmitNin_Verified_RecordsOneSuccess_EncryptsOntoIdentity_EmitsSubmittedAndSucceeded()
    {
        VerifierReturns(IdentityVerificationResult.Ok());
        _repo.Setup(r => r.GetKycStatusAsync(IdentityId, It.IsAny<CancellationToken>())).ReturnsAsync("verified");

        ComplianceRecordResponse record =
            await CreateSut().SubmitNinAsync(new SubmitNinRequest { Nin = "11122233344" }, CancellationToken.None);

        // exactly one attempt record; the ENCRYPTED nin; staff portal syncs staff (not parent).
        _repo.Verify(r => r.RecordSuccessAsync(IdentityId, "stub", "enc:11122233344", true, false, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.RecordFailureAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Published<IdentityVerificationSubmittedEvent>(Times.Once());
        Published<IdentityVerificationSucceededEvent>(Times.Once());
        Published<IdentityVerificationFailedEvent>(Times.Never());
        Assert.Equal("teacher", record.ActorType);
        Assert.Equal("verified", record.OverallStatus);
        Assert.Equal("verified", record.Steps[0].Status);
    }

    [Fact]
    public async Task SubmitNin_Verified_AsParent_SyncsParentNotStaff()
    {
        _context.Setup(c => c.UserType).Returns(UserTypes.Parent);
        VerifierReturns(IdentityVerificationResult.Ok());
        _repo.Setup(r => r.GetKycStatusAsync(IdentityId, It.IsAny<CancellationToken>())).ReturnsAsync("verified");

        ComplianceRecordResponse record =
            await CreateSut().SubmitNinAsync(new SubmitNinRequest { Nin = "11122233344" }, CancellationToken.None);

        _repo.Verify(r => r.RecordSuccessAsync(IdentityId, "stub", "enc:11122233344", false, true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("parent", record.ActorType);
    }

    [Fact]
    public async Task SubmitNin_VerificationFails_Throws422_RecordsOneFailure_EmitsFailedAndRejected_NoSuccess()
    {
        VerifierReturns(IdentityVerificationResult.Fail("NIN not found."));

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitNinAsync(new SubmitNinRequest { Nin = "11122233344" }, CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);
        _repo.Verify(r => r.RecordFailureAsync(IdentityId, "stub", "NIN not found.", It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.RecordSuccessAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        Published<IdentityVerificationSubmittedEvent>(Times.Once());
        Published<IdentityVerificationFailedEvent>(Times.Once());
        Published<IdentityVerificationRejectedEvent>(Times.Once());
        Published<IdentityVerificationSucceededEvent>(Times.Never());
    }

    [Fact]
    public async Task GetRecord_Unverified_MapsToNotStarted()
    {
        _repo.Setup(r => r.GetKycStatusAsync(IdentityId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        ComplianceRecordResponse record = await CreateSut().GetRecordAsync(CancellationToken.None);

        Assert.Equal("incomplete", record.OverallStatus);
        Assert.Equal("not_started", record.Steps[0].Status);
    }

    [Fact]
    public async Task GetRecord_Verified_MapsToVerified()
    {
        _repo.Setup(r => r.GetKycStatusAsync(IdentityId, It.IsAny<CancellationToken>())).ReturnsAsync("verified");

        ComplianceRecordResponse record = await CreateSut().GetRecordAsync(CancellationToken.None);

        Assert.Equal("verified", record.OverallStatus);
        Assert.Equal("verified", record.Steps[0].Status);
    }

    [Fact]
    public async Task NonComplianceActor_Throws403()
    {
        _context.Setup(c => c.UserType).Returns(UserTypes.School);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().GetRecordAsync(CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task SubmitNin_NoIdentityClaim_Throws401()
    {
        _context.Setup(c => c.IdentityId).Returns((string?)null);
        VerifierReturns(IdentityVerificationResult.Ok());

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SubmitNinAsync(new SubmitNinRequest { Nin = "11122233344" }, CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
    }
}
