namespace EduTech.Auth.SchoolOwner;

/// <summary>Phone-verification payload: the phone that registered + the 6-digit OTP it received.</summary>
public sealed class VerifyPhoneRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}
