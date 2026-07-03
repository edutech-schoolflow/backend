using EduTech.Fees;
using EduTech.Fees.Payments;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Persistence;
using Moq;

namespace EduTech.Auth.Tests.Fees;

public class ParentFeeServiceTests
{
    private readonly Mock<IParentFeeRepository> _repo = new();
    private readonly Mock<IPaymentProvider> _provider = new();
    private readonly Mock<IEduTechRequestContext> _context = new();
    private readonly Mock<IPlatformSettingsRepository> _settings = new();

    private static readonly Guid Parent = Guid.NewGuid();
    private static readonly Guid Student = Guid.NewGuid();
    private static readonly Guid FeeType = Guid.NewGuid();
    private static readonly string PinHash = BCrypt.Net.BCrypt.HashPassword("123456");

    private ParentFeeService CreateSut(decimal flatFee = 50m)
    {
        _context.SetupGet(c => c.UserId).Returns(Parent.ToString());
        _settings.Setup(s => s.GetDecimalAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(flatFee);
        return new ParentFeeService(_repo.Object, _provider.Object, _context.Object, _settings.Object);
    }

    private static PayableFeeRow Fee(FeeCategory category, decimal amount, decimal paid) =>
        new PayableFeeRow
        {
            SchoolId = Guid.NewGuid(), TermId = Guid.NewGuid(), Amount = amount,
            Category = SnakeCaseEnum.ToWire(category), Paid = paid
        };

    private void StubChargeSucceeds() =>
        _provider.Setup(p => p.ChargeAsync(It.IsAny<ChargeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChargeResult { ProviderReference = "STUB-1", Method = "stub", Succeeded = true });

    private static PayFeeRequest Pay(decimal amount, string pin = "123456") =>
        new PayFeeRequest { StudentId = Student, FeeTypeId = FeeType, Amount = amount, Pin = pin };

    [Fact]
    public async Task Pay_NoPinSet_Throws400()
    {
        _repo.Setup(r => r.GetPaymentPinHashAsync(Parent, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().PayAsync(Pay(1000), CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Pay_WrongPin_Throws401()
    {
        _repo.Setup(r => r.GetPaymentPinHashAsync(Parent, It.IsAny<CancellationToken>())).ReturnsAsync(PinHash);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().PayAsync(Pay(1000, "000000"), CancellationToken.None));
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task Pay_FeeNotApplicable_Throws404()
    {
        _repo.Setup(r => r.GetPaymentPinHashAsync(Parent, It.IsAny<CancellationToken>())).ReturnsAsync(PinHash);
        _repo.Setup(r => r.GetPayableFeeAsync(Parent, Student, FeeType, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PayableFeeRow?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().PayAsync(Pay(1000), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);   // not an approved fee applicable to a child the parent owns
    }

    [Fact]
    public async Task Pay_AlreadyFullyPaid_Throws409()
    {
        _repo.Setup(r => r.GetPaymentPinHashAsync(Parent, It.IsAny<CancellationToken>())).ReturnsAsync(PinHash);
        _repo.Setup(r => r.GetPayableFeeAsync(Parent, Student, FeeType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fee(FeeCategory.Compulsory, 1000m, 1000m));   // balance 0

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().PayAsync(Pay(500), CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Pay_AmountExceedsBalance_Throws400()
    {
        _repo.Setup(r => r.GetPaymentPinHashAsync(Parent, It.IsAny<CancellationToken>())).ReturnsAsync(PinHash);
        _repo.Setup(r => r.GetPayableFeeAsync(Parent, Student, FeeType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fee(FeeCategory.Compulsory, 1000m, 600m));   // balance 400

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().PayAsync(Pay(500), CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Pay_Compulsory_ComputesPlatformFee_RecordsWithoutSubscribing()
    {
        _repo.Setup(r => r.GetPaymentPinHashAsync(Parent, It.IsAny<CancellationToken>())).ReturnsAsync(PinHash);
        _repo.Setup(r => r.GetPayableFeeAsync(Parent, Student, FeeType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fee(FeeCategory.Compulsory, 1000m, 0m));
        StubChargeSucceeds();
        Guid paymentId = Guid.NewGuid();
        _repo.Setup(r => r.RecordPaymentAsync(Parent, Student, It.IsAny<Guid>(), FeeType, It.IsAny<Guid>(),
                1000m, 50m, 1050m, "stub", "STUB-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paymentId);

        PaymentResponse res = await CreateSut().PayAsync(Pay(1000), CancellationToken.None);

        Assert.Equal(paymentId, res.Id);
        Assert.Equal(FeeType, res.FeeTypeId);
        Assert.Equal(1000m, res.BaseAmount);
        Assert.Equal(50m, res.PlatformFee);          // flat ₦50 per payment
        Assert.Equal(1050m, res.TotalCharged);
        Assert.Equal(PaymentStatus.Successful, res.Status);
        _repo.Verify(r => r.RecordPaymentAsync(Parent, Student, It.IsAny<Guid>(), FeeType, It.IsAny<Guid>(),
            1000m, 50m, 1050m, "stub", "STUB-1", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pay_Optional_SubscribesChild()
    {
        _repo.Setup(r => r.GetPaymentPinHashAsync(Parent, It.IsAny<CancellationToken>())).ReturnsAsync(PinHash);
        _repo.Setup(r => r.GetPayableFeeAsync(Parent, Student, FeeType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fee(FeeCategory.Optional, 5000m, 0m));
        StubChargeSucceeds();
        _repo.Setup(r => r.RecordPaymentAsync(Parent, Student, It.IsAny<Guid>(), FeeType, It.IsAny<Guid>(),
                5000m, It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(),
                true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        await CreateSut().PayAsync(Pay(5000), CancellationToken.None);

        // Paying an optional fee subscribes the child to it.
        _repo.Verify(r => r.RecordPaymentAsync(Parent, Student, It.IsAny<Guid>(), FeeType, It.IsAny<Guid>(),
            5000m, It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(),
            true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
