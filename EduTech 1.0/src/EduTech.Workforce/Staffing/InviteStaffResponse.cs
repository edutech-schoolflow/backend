namespace EduTech.Workforce;

/// <summary>
/// Result of inviting a staff member. The link is also sent by SMS; it is returned here so the
/// admin can copy/share it directly (matches the frontend "copy invite link" behaviour).
/// </summary>
public sealed class InviteStaffResponse
{
    public required string InviteLink { get; init; }
    public required DateTime ExpiresAt { get; init; }
}
