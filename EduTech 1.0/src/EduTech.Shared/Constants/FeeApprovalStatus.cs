namespace EduTech.Shared.Constants;

/// <summary>
/// Approval state of a fee type. A staff-created fee is <c>pending_approval</c> until the school owner
/// approves it; an owner-created fee is <c>approved</c> on creation. Only approved fees are visible to
/// parents. Stored snake_case on <c>fee_types.approval_status</c>.
/// </summary>
public enum FeeApprovalStatus
{
    PendingApproval,
    Approved,
    Rejected
}
