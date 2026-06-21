namespace EduTech.Auth.Staff;

/// <summary>Invite details shown on the accept/welcome screen.</summary>
public sealed class InviteDetailsResponse
{
    public string? SchoolName { get; init; }
    public required string Role { get; init; }
    public required string EmploymentType { get; init; }
    public required DateTime ExpiresAt { get; init; }

    /// <summary>True if the invited phone already has a staff account (skip the password/OTP step).</summary>
    public required bool HasAccount { get; init; }
}

/// <summary>
/// Accept payload. New account: password + code (the OTP from send-otp) are required. Existing
/// account: the caller must be authenticated as that staff member; password/code are ignored.
/// </summary>
public sealed class AcceptInviteRequest
{
    public string Token { get; init; } = string.Empty;
    public string? Password { get; init; }
    public string? Code { get; init; }
}

/// <summary>Body carrying just an invite token (e.g. send-otp).</summary>
public sealed class InviteTokenRequest
{
    public string Token { get; init; } = string.Empty;
}
