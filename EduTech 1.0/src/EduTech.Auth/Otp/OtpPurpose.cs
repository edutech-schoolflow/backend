namespace EduTech.Auth.Otp;

/// <summary>
/// Every phone-OTP use-case in the system. The value is stored as <c>otp_codes.target_type</c>,
/// so together with <c>target_id</c> and <c>phone</c> each OTP row records its purpose,
/// which actor/record it is for, and who it was sent to.
///
/// Platform Admin does NOT appear here — admins use TOTP, not phone OTP.
/// </summary>
public static class OtpPurpose
{
    // Unified Identity (EDD-001)
    public const string IdentityPhoneVerification = "identity_phone_verification";
    public const string IdentityPasswordReset     = "identity_password_reset";

    // School Owner (Actor 1)
    public const string SchoolOwnerPhoneVerification = "school_owner_phone_verification";
    public const string SchoolOwnerPasswordReset     = "school_owner_password_reset";

    // Staff (Actor 2)
    public const string StaffPhoneVerification       = "staff_phone_verification";
    public const string StaffInviteVerification      = "staff_invite_verification";
    public const string StaffPasswordReset           = "staff_password_reset";

    // Parent (Actor 3)
    public const string ParentPhoneVerification      = "parent_phone_verification";
    public const string ParentPasswordReset          = "parent_password_reset";
    public const string ParentPaymentPinReset        = "parent_payment_pin_reset";
}
