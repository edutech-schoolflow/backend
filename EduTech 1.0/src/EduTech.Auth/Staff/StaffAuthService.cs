using EduTech.Auth.Otp;
using EduTech.Auth.RefreshTokens;
using EduTech.Auth.Security;
using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using EduTech.Shared.Phone;
using Npgsql;
using EduTech.Workforce;

namespace EduTech.Auth.Staff;

internal sealed class StaffAuthService : IStaffAuthService
{
    private const int MinPasswordLength = 8;
    private const int MaxFailedLogins = 5;
    private const int LockoutMinutes = 15;

    // Vague client message for duplicate/registration failures (anti-enumeration); the real reason
    // goes only to the logs via AppErrorException.LogReason.
    private const string RegistrationFailed = "Registration failed. Something went wrong.";

    private readonly IEduTechRequestContext _requestContext;
    private readonly IStaffUserRepository _staffRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IOtpService _otpService;
    private readonly INotificationDispatcher _notifications;
    private readonly IAccessTokenIssuer _accessTokenIssuer;
    private readonly IRefreshTokenService _refreshTokenService;

    public StaffAuthService(
        IEduTechRequestContext requestContext,
        IStaffUserRepository staffRepository,
        IPasswordHasher passwordHasher,
        IOtpService otpService,
        INotificationDispatcher notifications,
        IAccessTokenIssuer accessTokenIssuer,
        IRefreshTokenService refreshTokenService)
    {
        _requestContext = requestContext;
        _staffRepository = staffRepository;
        _passwordHasher = passwordHasher;
        _otpService = otpService;
        _notifications = notifications;
        _accessTokenIssuer = accessTokenIssuer;
        _refreshTokenService = refreshTokenService;
    }

    public async Task RegisterAsync(RegisterStaffRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new AppErrorException("First and last name are required.", 400, ErrorCodes.ValidationError);
        }

        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            throw new AppErrorException("Enter a valid Nigerian phone number.",
                400, ErrorCodes.ValidationError);
        }

        if (request.Password.Length < MinPasswordLength)
        {
            throw new AppErrorException($"Password must be at least {MinPasswordLength} characters.",
                400, ErrorCodes.ValidationError);
        }

        string? email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        if (await _staffRepository.ExistsByPhoneAsync(phone, cancellationToken))
        {
            throw new AppErrorException(RegistrationFailed, 409, ErrorCodes.PhoneTaken,
                logReason: "Staff registration blocked: phone already registered.");
        }

        if (email is not null && await _staffRepository.ExistsByEmailAsync(email, cancellationToken))
        {
            throw new AppErrorException(RegistrationFailed, 409, ErrorCodes.EmailTaken,
                logReason: "Staff registration blocked: email already in use.");
        }

        string passwordHash = _passwordHasher.Hash(request.Password);

        Guid staffUserId;
        try
        {
            staffUserId = await _staffRepository.CreateAsync(request.FirstName.Trim(),
                string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
                request.LastName.Trim(), phone, email, passwordHash, cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw MapUniqueViolation(ex);
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.StaffPhoneVerification, staffUserId, phone, cancellationToken);

        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow verification code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task VerifyPhoneAsync(StaffVerifyPhoneRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new AppErrorException("Phone and code are required.", 400, ErrorCodes.ValidationError);
        }

        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            throw new AppErrorException("Invalid verification request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Staff verify-phone: phone failed normalization.");
        }

        Guid? staffUserId = await _staffRepository.GetIdByPhoneAsync(phone, cancellationToken);
        if (staffUserId is null)
        {
            throw new AppErrorException("Invalid verification request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Staff verify-phone: no account for this phone.");
        }

        OtpVerifyResult result = await _otpService.VerifyAsync(
            OtpPurpose.StaffPhoneVerification, staffUserId.Value, request.Code.Trim(), cancellationToken);

        switch (result)
        {
            case OtpVerifyResult.Success:
                await _staffRepository.MarkPhoneVerifiedAsync(staffUserId.Value, cancellationToken);
                return;

            case OtpVerifyResult.Expired:
                throw new AppErrorException("Verification code has expired. Request a new one.",
                    400, ErrorCodes.OtpExpired, logReason: "Staff verify-phone: OTP expired.");

            case OtpVerifyResult.TooManyAttempts:
                throw new AppErrorException("Too many incorrect attempts. Request a new code.",
                    429, ErrorCodes.TooManyRequests, logReason: "Staff verify-phone: too many OTP attempts.");

            case OtpVerifyResult.NotFound:
                throw new AppErrorException("Invalid verification code.", 400, ErrorCodes.InvalidOtp,
                    logReason: "Staff verify-phone: no active OTP.");

            default: // Invalid
                throw new AppErrorException("Invalid verification code.", 400, ErrorCodes.InvalidOtp,
                    logReason: "Staff verify-phone: wrong code entered.");
        }
    }

    public async Task<StaffTokensResult> LoginAsync(StaffLoginRequest request, string? ipAddress,
        string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new AppErrorException("Phone and password are required.", 400, ErrorCodes.ValidationError);
        }

        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            throw new AppErrorException("Invalid phone or password.", 401, ErrorCodes.Unauthorized,
                logReason: "Staff login: phone failed normalization.");
        }

        StaffUserLoginRow? staff = await _staffRepository.GetByPhoneForLoginAsync(phone, cancellationToken);
        if (staff is null)
        {
            throw new AppErrorException("Invalid phone or password.", 401, ErrorCodes.Unauthorized,
                logReason: "Staff login: no account for this phone.");
        }

        if (!staff.IsActive)
        {
            throw new AppErrorException("This account is inactive.", 403, ErrorCodes.AccountInactive,
                logReason: "Staff login: account inactive.");
        }

        if (staff.LockedUntil is not null && staff.LockedUntil > DateTime.UtcNow)
        {
            throw new AppErrorException("Account locked after failed attempts. Try again later.",
                429, ErrorCodes.AccountLocked, logReason: "Staff login: account locked.");
        }

        if (staff.PasswordHash is null || !_passwordHasher.Verify(request.Password, staff.PasswordHash))
        {
            await ApplyFailedLoginAsync(staff, phone, cancellationToken);
            throw new AppErrorException("Invalid phone or password.", 401, ErrorCodes.Unauthorized,
                logReason: "Staff login: wrong password (or no password set).");
        }

        if (!staff.PhoneVerified)
        {
            throw new AppErrorException("Please verify your phone number before logging in.",
                403, ErrorCodes.PhoneNotVerified, logReason: "Staff login: phone not verified.");
        }

        await _staffRepository.ClearLoginFailuresAsync(staff.Id, cancellationToken);

        return await IssueTokensAsync(staff.Id, phone, staff.KycStatus, ipAddress, userAgent, cancellationToken);
    }

    public async Task<StaffTokensResult> RefreshAsync(string refreshToken, string? ipAddress,
        string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AppErrorException("Missing refresh token.", 401, ErrorCodes.Unauthorized,
                logReason: "Staff refresh: no refresh cookie present.");
        }

        RefreshRotationResult rotation = await _refreshTokenService.RotateAsync(
            refreshToken, ipAddress, userAgent, cancellationToken);

        if (!rotation.IsSuccess || rotation.ActorType != AuthActorTypes.Staff)
        {
            throw new AppErrorException("Session expired. Please log in again.", 401, ErrorCodes.Unauthorized,
                logReason: $"Staff refresh: rotation failed ({rotation.Status}).");
        }

        StaffUserTokenRow? staff = await _staffRepository.GetTokenClaimsAsync(rotation.ActorId, cancellationToken);
        if (staff is null || !staff.IsActive)
        {
            await _refreshTokenService.RevokeAllForActorAsync(
                AuthActorTypes.Staff, rotation.ActorId, cancellationToken);
            throw new AppErrorException("Session is no longer valid. Please log in again.",
                401, ErrorCodes.Unauthorized, logReason: "Staff refresh: account missing or deactivated.");
        }

        // RotateAsync already issued the replacement refresh token; reuse it for the response.
        AccessToken access = _accessTokenIssuer.IssueStaffIdentity(rotation.ActorId, staff.Phone, staff.KycStatus);

        return new StaffTokensResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = rotation.NewToken!,
            RefreshTokenExpiresAt = rotation.ExpiresAt
        };
    }

    public async Task ResendOtpAsync(StaffResendOtpRequest request, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            return;
        }

        StaffUserLoginRow? staff = await _staffRepository.GetByPhoneForLoginAsync(phone, cancellationToken);
        if (staff is null || staff.PhoneVerified)
        {
            return;
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.StaffPhoneVerification, staff.Id, phone, cancellationToken);
        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow verification code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task ForgotPasswordAsync(StaffForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            return;
        }

        Guid? staffUserId = await _staffRepository.GetIdByPhoneAsync(phone, cancellationToken);
        if (staffUserId is null)
        {
            return;
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.StaffPasswordReset, staffUserId.Value, phone, cancellationToken);
        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow password reset code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task ResetPasswordAsync(StaffResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (request.NewPassword.Length < MinPasswordLength)
        {
            throw new AppErrorException($"Password must be at least {MinPasswordLength} characters.",
                400, ErrorCodes.ValidationError);
        }

        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            throw new AppErrorException("Invalid reset request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Staff reset: phone failed normalization.");
        }

        Guid? staffUserId = await _staffRepository.GetIdByPhoneAsync(phone, cancellationToken);
        if (staffUserId is null)
        {
            throw new AppErrorException("Invalid reset request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Staff reset: no account for this phone.");
        }

        OtpVerifyResult result = await _otpService.VerifyAsync(
            OtpPurpose.StaffPasswordReset, staffUserId.Value, request.Code.Trim(), cancellationToken);
        if (result != OtpVerifyResult.Success)
        {
            throw result switch
            {
                OtpVerifyResult.Expired => new AppErrorException("Reset code has expired. Request a new one.",
                    400, ErrorCodes.OtpExpired, logReason: "Staff reset: OTP expired."),
                OtpVerifyResult.TooManyAttempts => new AppErrorException("Too many incorrect attempts. Request a new code.",
                    429, ErrorCodes.TooManyRequests, logReason: "Staff reset: too many OTP attempts."),
                _ => new AppErrorException("Invalid reset code.", 400, ErrorCodes.InvalidOtp,
                    logReason: "Staff reset: invalid or no active OTP.")
            };
        }

        string passwordHash = _passwordHasher.Hash(request.NewPassword);
        await _staffRepository.SetPasswordAsync(staffUserId.Value, passwordHash, cancellationToken);
        await _refreshTokenService.RevokeAllForActorAsync(AuthActorTypes.Staff, staffUserId.Value, cancellationToken);
    }

    public async Task<StaffMeResponse> GetMeAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_requestContext.UserId, out Guid staffUserId))
        {
            throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
        }

        StaffProfileRow profile = await _staffRepository.GetProfileAsync(staffUserId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);

        return new StaffMeResponse
        {
            FullName = profile.FullName,
            Phone = profile.Phone,
            Email = profile.Email,
            PhoneVerified = profile.PhoneVerified,
            KycStatus = profile.KycStatus
        };
    }

    private async Task<StaffTokensResult> IssueTokensAsync(Guid staffUserId, string phone, string kycStatus,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        AccessToken access = _accessTokenIssuer.IssueStaffIdentity(staffUserId, phone, kycStatus);

        RefreshTokenIssue refresh = await _refreshTokenService.IssueAsync(
            AuthActorTypes.Staff, staffUserId, ipAddress, userAgent, cancellationToken);

        return new StaffTokensResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = refresh.Token,
            RefreshTokenExpiresAt = refresh.ExpiresAt
        };
    }

    private async Task ApplyFailedLoginAsync(StaffUserLoginRow staff, string phone,
        CancellationToken cancellationToken)
    {
        int newCount = staff.FailedLoginCount + 1;
        DateTime? lockedUntil = null;

        if (newCount >= MaxFailedLogins)
        {
            lockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            newCount = 0;
        }

        await _staffRepository.SetLoginFailureAsync(staff.Id, newCount, lockedUntil, cancellationToken);

        if (lockedUntil is not null)
        {
            await _notifications.SendSmsAsync(phone,
                "Your SchoolFlow account was locked after multiple failed logins. " +
                "If this wasn't you, reset your password.", cancellationToken);
        }
    }

    private static AppErrorException MapUniqueViolation(PostgresException ex)
    {
        return ex.ConstraintName switch
        {
            "staff_users_phone_key" => new AppErrorException(RegistrationFailed, 409, ErrorCodes.PhoneTaken,
                logReason: "Unique violation: staff_users_phone_key"),
            "staff_users_email_key" => new AppErrorException(RegistrationFailed, 409, ErrorCodes.EmailTaken,
                logReason: "Unique violation: staff_users_email_key"),
            _ => new AppErrorException(RegistrationFailed, 409, ErrorCodes.Conflict,
                logReason: $"Unique violation: {ex.ConstraintName}")
        };
    }
}
