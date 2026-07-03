namespace EduTech.Shared.Constants;

/// <summary>
/// Whether a fee must be paid by every applicable student or is opt-in. Stored snake_case on
/// <c>fee_types.category</c>. Optional fees are subscribed to by paying them (e.g. lessons).
/// </summary>
public enum FeeCategory
{
    Compulsory,
    Optional
}
