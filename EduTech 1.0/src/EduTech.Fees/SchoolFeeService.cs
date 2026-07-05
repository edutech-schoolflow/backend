using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;

namespace EduTech.Fees;

public interface ISchoolFeeService
{
    Task<FeeTypeResponse> CreateFeeTypeAsync(CreateFeeTypeRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<FeeTypeResponse>> ListFeeTypesAsync(Guid? termId, string? approvalStatus, string? category, CancellationToken cancellationToken);
    Task<FeeTypeResponse> UpdateFeeTypeAsync(Guid feeTypeId, UpdateFeeTypeRequest request, CancellationToken cancellationToken);

    /// <summary>Removes a fee type: hard delete if it never billed anyone, otherwise archive. True = archived.</summary>
    Task<bool> DeleteFeeTypeAsync(Guid feeTypeId, CancellationToken cancellationToken);

    /// <summary>Owner-only: approve / reject a pending fee type.</summary>
    Task<FeeTypeResponse> ApproveFeeTypeAsync(Guid feeTypeId, CancellationToken cancellationToken);
    Task<FeeTypeResponse> RejectFeeTypeAsync(Guid feeTypeId, RejectFeeTypeRequest request, CancellationToken cancellationToken);

    Task<BursarCollectionsResponse> CollectionsAsync(Guid termId, CancellationToken cancellationToken);
}

internal sealed class SchoolFeeService : ISchoolFeeService
{
    private readonly ISchoolFeeRepository _repository;
    private readonly IEduTechRequestContext _context;

    public SchoolFeeService(ISchoolFeeRepository repository, IEduTechRequestContext context)
    {
        _repository = repository;
        _context = context;
    }

    public async Task<FeeTypeResponse> CreateFeeTypeAsync(CreateFeeTypeRequest request, CancellationToken cancellationToken)
    {
        string name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new AppErrorException("Fee type name is required.", 400, ErrorCodes.ValidationError);
        }

        if (request.Amount <= 0)
        {
            throw new AppErrorException("Amount must be greater than zero.", 400, ErrorCodes.ValidationError);
        }

        List<Guid> classIds = (request.ClassIds ?? new List<Guid>()).Where(c => c != Guid.Empty).Distinct().ToList();
        if (classIds.Count == 0)
        {
            throw new AppErrorException("Select at least one class this fee applies to.", 400, ErrorCodes.ValidationError);
        }

        if (!await _repository.TermExistsAsync(request.TermId, cancellationToken))
        {
            throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);
        }

        FeeCategory category = request.Category ?? FeeCategory.Compulsory;

        // Owner-created fees are approved immediately; staff-created fees wait for owner approval.
        bool ownerCreated = _context.IsOwner;
        FeeApprovalStatus status = ownerCreated ? FeeApprovalStatus.Approved : FeeApprovalStatus.PendingApproval;
        Guid? approvedBy = ownerCreated && Guid.TryParse(_context.UserId, out Guid uid) ? uid : null;

        Guid id = await _repository.CreateFeeTypeAsync(name, request.Amount, request.TermId,
            SnakeCaseEnum.ToWire(category), SnakeCaseEnum.ToWire(status), ownerCreated, approvedBy, classIds, cancellationToken);

        return new FeeTypeResponse
        {
            Id = id, Name = name, Amount = request.Amount, TermId = request.TermId,
            Category = category, ApprovalStatus = status, IsActive = true, ApplicableClassIds = classIds
        };
    }

    public async Task<IReadOnlyList<FeeTypeResponse>> ListFeeTypesAsync(Guid? termId, string? approvalStatus,
        string? category, CancellationToken cancellationToken)
    {
        string? statusFilter = SnakeCaseEnum.TryParse(approvalStatus, out FeeApprovalStatus s) ? SnakeCaseEnum.ToWire(s) : null;
        string? categoryFilter = SnakeCaseEnum.TryParse(category, out FeeCategory c) ? SnakeCaseEnum.ToWire(c) : null;
        IReadOnlyList<FeeTypeRow> rows = await _repository.ListFeeTypesAsync(termId, statusFilter, categoryFilter, cancellationToken);
        return rows.Select(MapFeeType).ToList();
    }

    public async Task<FeeTypeResponse> UpdateFeeTypeAsync(Guid feeTypeId, UpdateFeeTypeRequest request,
        CancellationToken cancellationToken)
    {
        string name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new AppErrorException("Fee type name is required.", 400, ErrorCodes.ValidationError);
        }

        if (request.Amount <= 0)
        {
            throw new AppErrorException("Amount must be greater than zero.", 400, ErrorCodes.ValidationError);
        }

        List<Guid> classIds = (request.ClassIds ?? new List<Guid>()).Where(c => c != Guid.Empty).Distinct().ToList();
        if (classIds.Count == 0)
        {
            throw new AppErrorException("Select at least one class this fee applies to.", 400, ErrorCodes.ValidationError);
        }

        FeeTypeRow existing = await _repository.GetFeeTypeAsync(feeTypeId, cancellationToken)
            ?? throw new AppErrorException("Fee type not found.", 404, ErrorCodes.NotFound);

        // Approved fees are locked (they may already be visible to / paid by parents). Edit pending/rejected only.
        if (SnakeCaseEnum.Parse<FeeApprovalStatus>(existing.ApprovalStatus) == FeeApprovalStatus.Approved)
        {
            throw new AppErrorException(
                "Approved fees are locked — archive this one and create a new fee instead.", 409, ErrorCodes.Conflict);
        }

        FeeCategory category = request.Category ?? SnakeCaseEnum.Parse<FeeCategory>(existing.Category);
        await _repository.UpdateFeeTypeAsync(feeTypeId, name, request.Amount, SnakeCaseEnum.ToWire(category), classIds, cancellationToken);
        return MapFeeType((await _repository.GetFeeTypeAsync(feeTypeId, cancellationToken))!);
    }

    public async Task<FeeTypeResponse> ApproveFeeTypeAsync(Guid feeTypeId, CancellationToken cancellationToken)
    {
        FeeApprovalStatus current = await RequireOwnerAndGetStatusAsync(feeTypeId, FeeApprovalStatus.Approved, cancellationToken);
        if (current == FeeApprovalStatus.Approved)
        {
            return MapFeeType((await _repository.GetFeeTypeAsync(feeTypeId, cancellationToken))!);   // idempotent
        }

        Guid.TryParse(_context.UserId, out Guid uid);
        if (await _repository.ApproveAsync(feeTypeId, uid, cancellationToken) == 0)
        {
            throw new AppErrorException("Fee status changed, please retry.", 409, ErrorCodes.Conflict);
        }

        return MapFeeType((await _repository.GetFeeTypeAsync(feeTypeId, cancellationToken))!);
    }

    public async Task<FeeTypeResponse> RejectFeeTypeAsync(Guid feeTypeId, RejectFeeTypeRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAndGetStatusAsync(feeTypeId, FeeApprovalStatus.Rejected, cancellationToken);

        if (await _repository.RejectAsync(feeTypeId, request.Reason?.Trim(), cancellationToken) == 0)
        {
            throw new AppErrorException("Fee status changed, please retry.", 409, ErrorCodes.Conflict);
        }

        return MapFeeType((await _repository.GetFeeTypeAsync(feeTypeId, cancellationToken))!);
    }

    /// <summary>Approve/reject are OWNER-only; staff (even with manage_fees) get 403. Guards the transition.</summary>
    private async Task<FeeApprovalStatus> RequireOwnerAndGetStatusAsync(Guid feeTypeId, FeeApprovalStatus target,
        CancellationToken cancellationToken)
    {
        if (!_context.IsOwner)
        {
            throw new AppErrorException("Only the school owner can approve or reject fees.", 403, ErrorCodes.AccessDenied);
        }

        FeeTypeRow existing = await _repository.GetFeeTypeAsync(feeTypeId, cancellationToken)
            ?? throw new AppErrorException("Fee type not found.", 404, ErrorCodes.NotFound);

        FeeApprovalStatus current = SnakeCaseEnum.Parse<FeeApprovalStatus>(existing.ApprovalStatus);
        FeeApprovalLifecycle.Rules.Require(current, target);   // 409 on an illegal transition
        return current;
    }

    public async Task<bool> DeleteFeeTypeAsync(Guid feeTypeId, CancellationToken cancellationToken)
    {
        if (await _repository.GetFeeTypeAsync(feeTypeId, cancellationToken) is null)
        {
            throw new AppErrorException("Fee type not found.", 404, ErrorCodes.NotFound);
        }

        // Once a payment has been recorded against a fee it is permanent financial history — archiving it
        // would silently drop it from what students owe, so it can be neither archived nor deleted.
        if (await _repository.FeeTypeHasPaymentsAsync(feeTypeId, cancellationToken))
        {
            throw new AppErrorException(
                "This fee already has payments recorded against it, so it can't be archived or deleted.",
                409, ErrorCodes.Conflict);
        }

        // Subscribed but not yet paid -> archive (keep the record + audit link). Never used -> hard delete.
        if (await _repository.FeeTypeIsUsedAsync(feeTypeId, cancellationToken))
        {
            await _repository.ArchiveFeeTypeAsync(feeTypeId, cancellationToken);
            return true;
        }

        await _repository.DeleteFeeTypeAsync(feeTypeId, cancellationToken);
        return false;
    }

    private static FeeTypeResponse MapFeeType(FeeTypeRow r) => new FeeTypeResponse
    {
        Id = r.Id, Name = r.Name, Amount = r.Amount, TermId = r.TermId,
        Category = SnakeCaseEnum.Parse<FeeCategory>(r.Category),
        ApprovalStatus = SnakeCaseEnum.Parse<FeeApprovalStatus>(r.ApprovalStatus),
        RejectionReason = r.RejectionReason, IsActive = r.IsActive, ApplicableClassIds = SplitGuids(r.ClassIds)
    };

    public async Task<BursarCollectionsResponse> CollectionsAsync(Guid termId, CancellationToken cancellationToken)
    {
        IReadOnlyList<FeeCollectionRow> rows = await _repository.CollectionsAsync(termId, cancellationToken);

        List<FeeCollectionLine> lines = rows.Select(r =>
        {
            decimal expected = r.Amount * r.ApplicableCount;   // compulsory: applicable students; optional: subscribers
            return new FeeCollectionLine
            {
                FeeTypeId = r.FeeTypeId, Name = r.Name, Category = SnakeCaseEnum.Parse<FeeCategory>(r.Category),
                Amount = r.Amount, Expected = expected, Collected = r.Collected,
                Outstanding = Math.Max(0m, expected - r.Collected), Payers = r.Payers, ApplicableCount = r.ApplicableCount
            };
        }).ToList();

        return new BursarCollectionsResponse
        {
            TotalExpected = lines.Sum(l => l.Expected),
            TotalCollected = lines.Sum(l => l.Collected),
            TotalOutstanding = lines.Sum(l => l.Outstanding),
            ByFee = lines
        };
    }

    private static IReadOnlyList<Guid> SplitGuids(string? joined) =>
        string.IsNullOrWhiteSpace(joined)
            ? Array.Empty<Guid>()
            : joined.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
}
