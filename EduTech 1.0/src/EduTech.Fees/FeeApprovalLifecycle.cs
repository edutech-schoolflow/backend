using EduTech.Shared.Constants;
using EduTech.Shared.Lifecycle;

namespace EduTech.Fees;

/// <summary>
/// Approval lifecycle for a fee type: a pending fee is approved or rejected by the owner; a rejected fee
/// returns to pending when the staff member edits/resubmits it. The guard gives a clean 409 on an
/// illegal move (e.g. approving an already-approved fee, or re-deciding a rejected one out of order).
/// </summary>
internal static class FeeApprovalLifecycle
{
    public static readonly StateTransitions<FeeApprovalStatus> Rules = new(
        new Dictionary<FeeApprovalStatus, IReadOnlySet<FeeApprovalStatus>>
        {
            [FeeApprovalStatus.PendingApproval] = Set(FeeApprovalStatus.Approved, FeeApprovalStatus.Rejected),
            [FeeApprovalStatus.Rejected]        = Set(FeeApprovalStatus.PendingApproval),
            [FeeApprovalStatus.Approved]        = Set(),
        });

    private static IReadOnlySet<FeeApprovalStatus> Set(params FeeApprovalStatus[] states) =>
        new HashSet<FeeApprovalStatus>(states);
}
