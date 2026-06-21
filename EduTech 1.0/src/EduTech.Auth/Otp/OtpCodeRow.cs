namespace EduTech.Auth.Otp;

/// <summary>Row shape read back from <c>otp_codes</c> during verification.</summary>
internal sealed class OtpCodeRow
{
    public Guid Id { get; init; }
    public string CodeHash { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public int Attempts { get; init; }
}
