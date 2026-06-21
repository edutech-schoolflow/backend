namespace EduTech.Shared.Constants;

/// <summary>
/// Integer error codes returned to clients as <c>ApiError.ErrorCode</c> (kedco-style catalog).
/// Grouped by range: 1000s generic, 1100s account/auth, 9000s internal.
/// </summary>
public static class ErrorCodes
{
    // ── Generic (1000–1099) ──────────────────────────────────────────────────
    public const int ValidationError = 1000;
    public const int Unauthorized    = 1001;
    public const int Forbidden       = 1002;
    public const int NotFound        = 1003;
    public const int Conflict        = 1004;
    public const int TooManyRequests = 1005;
    public const int AccessDenied    = 1006;
    public const int FeatureDisabled = 1007;
    public const int MaintenanceMode = 1008;

    // ── Account / auth (1100–1199) ───────────────────────────────────────────
    public const int PhoneTaken       = 1100;
    public const int EmailTaken       = 1101;
    public const int InvalidOtp       = 1102;
    public const int OtpExpired       = 1103;
    public const int PhoneNotVerified = 1104;
    public const int AccountInactive  = 1105;
    public const int AccountLocked    = 1106;
    public const int InviteExpired    = 1107;
    public const int InviteInvalid    = 1108;
    public const int SubdomainTaken   = 1109;
    public const int RegistrationClosed = 1110;

    // ── Internal (9000+) ─────────────────────────────────────────────────────
    public const int Unknown = 9000;
}
