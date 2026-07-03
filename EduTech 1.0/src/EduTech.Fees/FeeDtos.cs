using EduTech.Shared.Constants;

namespace EduTech.Fees;

// ---- fee types (school) ----

public sealed class CreateFeeTypeRequest
{
    public string Name { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public Guid TermId { get; init; }
    public FeeCategory? Category { get; init; }                     // compulsory (default) | optional
    public List<Guid> ClassIds { get; init; } = new List<Guid>();   // which classes it applies to
}

public sealed class FeeTypeResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required decimal Amount { get; init; }
    public required Guid TermId { get; init; }
    public required FeeCategory Category { get; init; }
    public required FeeApprovalStatus ApprovalStatus { get; init; }
    public string? RejectionReason { get; init; }
    public bool IsActive { get; init; } = true;     // false once archived
    public required IReadOnlyList<Guid> ApplicableClassIds { get; init; }
}

/// <summary>Edit a fee type. Allowed only while pending_approval/rejected (approved fees are locked).</summary>
public sealed class UpdateFeeTypeRequest
{
    public string Name { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public FeeCategory? Category { get; init; }
    public List<Guid> ClassIds { get; init; } = new List<Guid>();
}

public sealed class RejectFeeTypeRequest
{
    public string? Reason { get; init; }
}

// ---- collections (school, payment-based) ----

public sealed class FeeCollectionLine
{
    public required Guid FeeTypeId { get; init; }
    public required string Name { get; init; }
    public required FeeCategory Category { get; init; }
    public required decimal Amount { get; init; }
    public required decimal Expected { get; init; }          // amount × applicable/subscribed students
    public required decimal Collected { get; init; }         // Σ successful payments toward this fee
    public required decimal Outstanding { get; init; }       // max(0, expected − collected)
    public required int Payers { get; init; }                // distinct students who have paid anything
    public required int ApplicableCount { get; init; }       // compulsory: applicable students; optional: subscribers
}

public sealed class BursarCollectionsResponse
{
    public required decimal TotalExpected { get; init; }
    public required decimal TotalCollected { get; init; }
    public required decimal TotalOutstanding { get; init; }
    public required IReadOnlyList<FeeCollectionLine> ByFee { get; init; }
}

// ---- parent ----

/// <summary>A single fee applicable to a child (compulsory always owed; optional owed once subscribed).</summary>
public sealed class ChildFeeItemResponse
{
    public required Guid FeeTypeId { get; init; }
    public required string Name { get; init; }
    public required FeeCategory Category { get; init; }
    public required decimal Amount { get; init; }
    public required decimal Paid { get; init; }
    public required decimal Balance { get; init; }
    public required bool Subscribed { get; init; }
}

/// <summary>A child's payable fees (approved fee types for the child's class + current term).</summary>
public sealed class ChildFeesResponse
{
    public required Guid StudentId { get; init; }
    public string? StudentName { get; init; }
    public string? SchoolName { get; init; }
    public string? ClassName { get; init; }
    public string? TermName { get; init; }
    public required decimal OutstandingCompulsory { get; init; }   // Σ unpaid compulsory
    public required IReadOnlyList<ChildFeeItemResponse> Fees { get; init; }
}

/// <summary>Pay (part of) one fee type for one child. Paying an optional fee subscribes the child to it.</summary>
public sealed class PayFeeRequest
{
    public Guid StudentId { get; init; }
    public Guid FeeTypeId { get; init; }
    public decimal Amount { get; init; }                // toward the fee (flat platform fee added on top)
    public string Pin { get; init; } = string.Empty;   // 6-digit payment PIN
}

public sealed class PaymentResponse
{
    public required Guid Id { get; init; }
    public Guid? FeeTypeId { get; init; }
    public required decimal BaseAmount { get; init; }
    public required decimal PlatformFee { get; init; }
    public required decimal TotalCharged { get; init; }
    public required string Method { get; init; }
    public required string Reference { get; init; }
    public required PaymentStatus Status { get; init; }
    public DateTime? PaidAt { get; init; }
}
