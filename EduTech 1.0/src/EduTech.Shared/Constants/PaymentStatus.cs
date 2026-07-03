namespace EduTech.Shared.Constants;

/// <summary>A payment's state — a fixed, closed set. Stored as snake_case on <c>payments.status</c>.</summary>
public enum PaymentStatus
{
    Pending,
    Successful,
    Failed
}
