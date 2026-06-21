namespace EduTech.Auth.Otp;

/// <summary>Outcome of verifying a submitted OTP. The caller maps this to an HTTP response.</summary>
public enum OtpVerifyResult
{
    Success,
    Invalid,
    Expired,
    TooManyAttempts,
    NotFound
}
