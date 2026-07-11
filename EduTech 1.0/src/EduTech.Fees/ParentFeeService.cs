using EduTech.Fees.Payments;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Persistence;

namespace EduTech.Fees;

public interface IParentFeeService
{
    Task<IReadOnlyList<ChildFeesResponse>> GetFeesAsync(Guid? studentId, CancellationToken cancellationToken);
    Task<PaymentResponse> PayAsync(PayFeeRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentResponse>> ListPaymentsAsync(CancellationToken cancellationToken);

    /// <summary>The signed-in IDENTITY's payments across schools (EDD-002 identity-space view) —
    /// includes application fees paid before any membership. Empty until they've paid anything.</summary>
    Task<IReadOnlyList<PaymentResponse>> ListMyPaymentsAsync(CancellationToken cancellationToken);
}

internal sealed class ParentFeeService : IParentFeeService
{
    private readonly IParentFeeRepository _repository;
    private readonly IPaymentProvider _provider;
    private readonly IEduTechRequestContext _context;
    private readonly IPlatformSettingsRepository _settings;

    public ParentFeeService(IParentFeeRepository repository, IPaymentProvider provider,
        IEduTechRequestContext context, IPlatformSettingsRepository settings)
    {
        _repository = repository;
        _provider = provider;
        _context = context;
        _settings = settings;
    }

    public async Task<IReadOnlyList<ChildFeesResponse>> GetFeesAsync(Guid? studentId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChildFeeLineRow> rows = await _repository.GetChildFeesAsync(ParentId, studentId, cancellationToken);

        return rows.GroupBy(r => r.StudentId).Select(g =>
        {
            ChildFeeLineRow first = g.First();
            List<ChildFeeItemResponse> items = g.Select(r => new ChildFeeItemResponse
            {
                FeeTypeId = r.FeeTypeId, Name = r.FeeName, Category = SnakeCaseEnum.Parse<FeeCategory>(r.Category),
                Amount = r.Amount, Paid = r.Paid, Balance = Math.Max(0m, r.Amount - r.Paid), Subscribed = r.Subscribed
            }).ToList();

            return new ChildFeesResponse
            {
                StudentId = g.Key, StudentName = first.StudentName, SchoolName = first.SchoolName,
                ClassName = first.ClassName, TermName = first.TermName,
                OutstandingCompulsory = items.Where(i => i.Category == FeeCategory.Compulsory).Sum(i => i.Balance),
                Fees = items
            };
        }).ToList();
    }

    public async Task<PaymentResponse> PayAsync(PayFeeRequest request, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
        {
            throw new AppErrorException("Amount must be greater than zero.", 400, ErrorCodes.ValidationError);
        }

        // Payment PIN (separate from login password) authorizes the charge.
        string? pinHash = await _repository.GetPaymentPinHashAsync(ParentId, cancellationToken);
        if (string.IsNullOrEmpty(pinHash))
        {
            throw new AppErrorException("Set a payment PIN before paying.", 400, ErrorCodes.ValidationError);
        }

        if (string.IsNullOrWhiteSpace(request.Pin) || !BCrypt.Net.BCrypt.Verify(request.Pin.Trim(), pinHash))
        {
            throw new AppErrorException("Incorrect payment PIN.", 401, ErrorCodes.Unauthorized,
                logReason: $"Parent {ParentId} wrong payment PIN on fee {request.FeeTypeId}.");
        }

        // Must be an approved fee applicable to a child the parent owns.
        PayableFeeRow fee = await _repository.GetPayableFeeAsync(ParentId, request.StudentId, request.FeeTypeId, cancellationToken)
            ?? throw new AppErrorException("Fee not found for this child.", 404, ErrorCodes.NotFound);

        decimal balance = fee.Amount - fee.Paid;
        if (balance <= 0)
        {
            throw new AppErrorException("This fee is already fully paid.", 409, ErrorCodes.Conflict);
        }

        if (request.Amount > balance)
        {
            throw new AppErrorException("Amount exceeds the outstanding balance for this fee.", 400, ErrorCodes.ValidationError);
        }

        // Flat per-transaction platform fee (admin-set), added on top.
        decimal platformFee = await _settings.GetDecimalAsync(PlatformSettingKeys.PaymentPlatformFee, 0m, cancellationToken);
        decimal totalCharged = request.Amount + platformFee;

        ChargeResult charge = await _provider.ChargeAsync(new ChargeRequest
        {
            ParentId = ParentId, SchoolId = fee.SchoolId, TotalCharged = totalCharged,
            Reference = Guid.NewGuid().ToString("N")
        }, cancellationToken);

        if (!charge.Succeeded)
        {
            throw new AppErrorException("Payment is awaiting confirmation.", 502, ErrorCodes.Unknown);
        }

        // Paying an optional fee subscribes the child to it.
        bool subscribeOptional = SnakeCaseEnum.Parse<FeeCategory>(fee.Category) == FeeCategory.Optional;

        Guid paymentId = await _repository.RecordPaymentAsync(ParentId, request.StudentId, fee.SchoolId,
            request.FeeTypeId, fee.TermId, request.Amount, platformFee, totalCharged, charge.Method,
            charge.ProviderReference, subscribeOptional, cancellationToken);

        return new PaymentResponse
        {
            Id = paymentId, FeeTypeId = request.FeeTypeId, BaseAmount = request.Amount, PlatformFee = platformFee,
            TotalCharged = totalCharged, Method = charge.Method, Reference = charge.ProviderReference,
            Status = PaymentStatus.Successful, PaidAt = DateTime.UtcNow
        };
    }

    public Task<IReadOnlyList<PaymentResponse>> ListPaymentsAsync(CancellationToken cancellationToken)
        => FetchPaymentsAsync(ParentId, cancellationToken);

    public async Task<IReadOnlyList<PaymentResponse>> ListMyPaymentsAsync(CancellationToken cancellationToken)
    {
        Guid? parentId = await _repository.GetParentIdByIdentityAsync(CurrentIdentityId, cancellationToken);
        return parentId is Guid pid
            ? await FetchPaymentsAsync(pid, cancellationToken)
            : Array.Empty<PaymentResponse>();
    }

    private async Task<IReadOnlyList<PaymentResponse>> FetchPaymentsAsync(Guid parentId, CancellationToken cancellationToken)
    {
        IReadOnlyList<PaymentRow> rows = await _repository.ListPaymentsAsync(parentId, cancellationToken);
        return rows.Select(r => new PaymentResponse
        {
            Id = r.Id, FeeTypeId = r.FeeTypeId, BaseAmount = r.BaseAmount, PlatformFee = r.PlatformFee,
            TotalCharged = r.TotalCharged, Method = r.Method, Reference = r.MonnifyReference,
            Status = SnakeCaseEnum.Parse<PaymentStatus>(r.Status), PaidAt = r.PaidAt
        }).ToList();
    }

    private Guid ParentId =>
        Guid.TryParse(_context.UserId, out Guid id)
            ? id
            : throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);

    // The identity behind the session: identity_id claim (org tokens) or user_id (identity session).
    private Guid CurrentIdentityId =>
        Guid.TryParse(_context.IdentityId ?? _context.UserId, out Guid id)
            ? id
            : throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
}
