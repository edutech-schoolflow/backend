namespace EduTech.Notifications;

/// <summary>
/// The SMS to deliver, stashed in the cache under an opaque key so the body (which may contain an
/// OTP) never lands in Hangfire's job arguments — the enqueued job carries only the key
/// (claim-check pattern).
/// </summary>
public sealed class SmsPayload
{
    public string Phone { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
