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

namespace EduTech.Auth.Parent;

/// <summary>Parent (Actor 3) auth: standalone register → verify → login → refresh, plus payment PIN.</summary>
public interface IParentAuthService
{
    Task RegisterAsync(RegisterParentRequest request, CancellationToken cancellationToken);
    Task VerifyPhoneAsync(ParentVerifyPhoneRequest request, CancellationToken cancellationToken);
    Task<ParentTokensResult> LoginAsync(ParentLoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken);
    Task<ParentTokensResult> RefreshAsync(string refreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken);

    /// <summary>Sets the authenticated parent's 6-digit payment PIN.</summary>
    Task SetPaymentPinAsync(SetPaymentPinRequest request, CancellationToken cancellationToken);

    Task ResendOtpAsync(ParentResendOtpRequest request, CancellationToken cancellationToken);
    Task ForgotPasswordAsync(ParentForgotPasswordRequest request, CancellationToken cancellationToken);
    Task ResetPasswordAsync(ParentResetPasswordRequest request, CancellationToken cancellationToken);
    Task<ParentMeResponse> GetMeAsync(CancellationToken cancellationToken);
}

internal sealed class ParentAuthService : IParentAuthService
{
    private const int MinPasswordLength = 8;
    private const int PinLength = 6;
    private const int MaxFailedLogins = 5;
    private const int LockoutMinutes = 15;

    private const string RegistrationFailed = "Registration failed. Something went wrong.";

    private readonly IEduTechRequestContext _requestContext;
    private readonly IParentRepository _parents;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IOtpService _otpService;
    private readonly INotificationDispatcher _notifications;
    private readonly IAccessTokenIssuer _accessTokenIssuer;
    private readonly IRefreshTokenService _refreshTokenService;

    public ParentAuthService(
        IEduTechRequestContext requestContext,
        IParentRepository parents,
        IPasswordHasher passwordHasher,
        IOtpService otpService,
        INotificationDispatcher notifications,
        IAccessTokenIssuer accessTokenIssuer,
        IRefreshTokenService refreshTokenService)
    {
        _requestContext = requestContext;
        _parents = parents;
        _passwordHasher = passwordHasher;
        _otpService = otpService;
        _notifications = notifications;
        _accessTokenIssuer = accessTokenIssuer;
        _refreshTokenService = refreshTokenService;
    }

    public async Task RegisterAsync(RegisterParentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new AppErrorException("First and last name are required.", 400, ErrorCodes.ValidationError);
        }

        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            throw new AppErrorException("Enter a valid Nigerian phone number.", 400, ErrorCodes.ValidationError);
        }

        if (request.Password.Length < MinPasswordLength)
        {
            throw new AppErrorException($"Password must be at least {MinPasswordLength} characters.",
                400, ErrorCodes.ValidationError);
        }

        string? email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

       
        ParentClaimState? existing = await _parents.GetClaimStateByPhoneAsync(phone, cancellationToken);
        if (existing is not null && existing.HasPassword)
        {
            throw new AppErrorException(RegistrationFailed, 409, ErrorCodes.PhoneTaken,
                logReason: "Parent registration blocked: phone already registered.");
        }

        if (email is not null && await _parents.ExistsByEmailAsync(email, cancellationToken))
        {
            throw new AppErrorException(RegistrationFailed, 409, ErrorCodes.EmailTaken,
                logReason: "Parent registration blocked: email already in use.");
        }

        string passwordHash = _passwordHasher.Hash(request.Password);
        string firstName = request.FirstName.Trim();
        string? middleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim();
        string lastName = request.LastName.Trim();

        Guid parentId;
        if (existing is not null)
        {
            int claimed = await _parents.ClaimAsync(existing.Id, firstName, middleName, lastName,
                email, passwordHash, cancellationToken);
            if (claimed == 0)
            {
                throw new AppErrorException(RegistrationFailed, 409, ErrorCodes.PhoneTaken,
                    logReason: "Parent registration blocked: pending account was claimed concurrently.");
            }
            parentId = existing.Id;
        }
        else
        {
            try
            {
                parentId = await _parents.CreateAsync(firstName, middleName, lastName, phone, email,
                    passwordHash, cancellationToken);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw MapUniqueViolation(ex);
            }
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.ParentPhoneVerification, parentId, phone, cancellationToken);

        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow verification code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task VerifyPhoneAsync(ParentVerifyPhoneRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new AppErrorException("Phone and code are required.", 400, ErrorCodes.ValidationError);
        }

        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            throw new AppErrorException("Invalid verification request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Parent verify-phone: phone failed normalization.");
        }

        Guid? parentId = await _parents.GetIdByPhoneAsync(phone, cancellationToken);
        if (parentId is null)
        {
            throw new AppErrorException("Invalid verification request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Parent verify-phone: no account for this phone.");
        }

        OtpVerifyResult result = await _otpService.VerifyAsync(
            OtpPurpose.ParentPhoneVerification, parentId.Value, request.Code.Trim(), cancellationToken);

        switch (result)
        {
            case OtpVerifyResult.Success:
                await _parents.MarkPhoneVerifiedAsync(parentId.Value, cancellationToken);
                return;

            case OtpVerifyResult.Expired:
                throw new AppErrorException("Verification code has expired. Request a new one.",
                    400, ErrorCodes.OtpExpired, logReason: "Parent verify-phone: OTP expired.");

            case OtpVerifyResult.TooManyAttempts:
                throw new AppErrorException("Too many incorrect attempts. Request a new code.",
                    429, ErrorCodes.TooManyRequests, logReason: "Parent verify-phone: too many OTP attempts.");

            default:
                throw new AppErrorException("Invalid verification code.", 400, ErrorCodes.InvalidOtp,
                    logReason: "Parent verify-phone: invalid or no active OTP.");
        }
    }

    public async Task<ParentTokensResult> LoginAsync(ParentLoginRequest request, string? ipAddress,
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
                logReason: "Parent login: phone failed normalization.");
        }

        ParentLoginRow? parent = await _parents.GetByPhoneForLoginAsync(phone, cancellationToken);
        if (parent is null)
        {
            throw new AppErrorException("Invalid phone or password.", 401, ErrorCodes.Unauthorized,
                logReason: "Parent login: no account for this phone.");
        }

        if (!parent.IsActive)
        {
            throw new AppErrorException("This account is inactive.", 403, ErrorCodes.AccountInactive,
                logReason: "Parent login: account inactive.");
        }

        if (parent.LockedUntil is not null && parent.LockedUntil > DateTime.UtcNow)
        {
            throw new AppErrorException("Account locked after failed attempts. Try again later.",
                429, ErrorCodes.AccountLocked, logReason: "Parent login: account locked.");
        }

        if (parent.PasswordHash is null || !_passwordHasher.Verify(request.Password, parent.PasswordHash))
        {
            await ApplyFailedLoginAsync(parent, phone, cancellationToken);
            throw new AppErrorException("Invalid phone or password.", 401, ErrorCodes.Unauthorized,
                logReason: "Parent login: wrong password (or no password set).");
        }

        if (!parent.PhoneVerified)
        {
            throw new AppErrorException("Please verify your phone number before logging in.",
                403, ErrorCodes.PhoneNotVerified, logReason: "Parent login: phone not verified.");
        }

        await _parents.ClearLoginFailuresAsync(parent.Id, cancellationToken);
        return await IssueTokensAsync(parent.Id, phone, ipAddress, userAgent, cancellationToken);
    }

    public async Task<ParentTokensResult> RefreshAsync(string refreshToken, string? ipAddress,
        string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AppErrorException("Missing refresh token.", 401, ErrorCodes.Unauthorized,
                logReason: "Parent refresh: no refresh cookie.");
        }

        RefreshRotationResult rotation = await _refreshTokenService.RotateAsync(
            refreshToken, ipAddress, userAgent, cancellationToken);

        if (!rotation.IsSuccess || rotation.ActorType != AuthActorTypes.Parent)
        {
            throw new AppErrorException("Session expired. Please log in again.", 401, ErrorCodes.Unauthorized,
                logReason: $"Parent refresh: rotation failed ({rotation.Status}).");
        }

        ParentTokenRow? parent = await _parents.GetTokenClaimsAsync(rotation.ActorId, cancellationToken);
        if (parent is null || !parent.IsActive)
        {
            await _refreshTokenService.RevokeAllForActorAsync(
                AuthActorTypes.Parent, rotation.ActorId, cancellationToken);
            throw new AppErrorException("Session is no longer valid. Please log in again.",
                401, ErrorCodes.Unauthorized, logReason: "Parent refresh: account missing or deactivated.");
        }

        AccessToken access = _accessTokenIssuer.IssueParent(rotation.ActorId, parent.Phone);

        return new ParentTokensResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = rotation.NewToken!,
            RefreshTokenExpiresAt = rotation.ExpiresAt
        };
    }

    public async Task SetPaymentPinAsync(SetPaymentPinRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_requestContext.UserId, out Guid parentId))
        {
            throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
        }

        if (request.Pin is null || request.Pin.Length != PinLength || !request.Pin.All(char.IsDigit))
        {
            throw new AppErrorException("PIN must be 6 digits.", 400, ErrorCodes.ValidationError);
        }

        string pinHash = _passwordHasher.Hash(request.Pin);
        await _parents.SetPaymentPinAsync(parentId, pinHash, cancellationToken);
    }

    public async Task ResendOtpAsync(ParentResendOtpRequest request, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            return;
        }

        ParentLoginRow? parent = await _parents.GetByPhoneForLoginAsync(phone, cancellationToken);
        if (parent is null || parent.PhoneVerified)
        {
            return;
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.ParentPhoneVerification, parent.Id, phone, cancellationToken);
        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow verification code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task ForgotPasswordAsync(ParentForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            return;
        }

        Guid? parentId = await _parents.GetIdByPhoneAsync(phone, cancellationToken);
        if (parentId is null)
        {
            return;
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.ParentPasswordReset, parentId.Value, phone, cancellationToken);
        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow password reset code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task ResetPasswordAsync(ParentResetPasswordRequest request, CancellationToken cancellationToken)
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
                logReason: "Parent reset: phone failed normalization.");
        }

        Guid? parentId = await _parents.GetIdByPhoneAsync(phone, cancellationToken);
        if (parentId is null)
        {
            throw new AppErrorException("Invalid reset request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Parent reset: no account for this phone.");
        }

        OtpVerifyResult result = await _otpService.VerifyAsync(
            OtpPurpose.ParentPasswordReset, parentId.Value, request.Code.Trim(), cancellationToken);
        if (result != OtpVerifyResult.Success)
        {
            throw result switch
            {
                OtpVerifyResult.Expired => new AppErrorException("Reset code has expired. Request a new one.",
                    400, ErrorCodes.OtpExpired, logReason: "Parent reset: OTP expired."),
                OtpVerifyResult.TooManyAttempts => new AppErrorException("Too many incorrect attempts. Request a new code.",
                    429, ErrorCodes.TooManyRequests, logReason: "Parent reset: too many OTP attempts."),
                _ => new AppErrorException("Invalid reset code.", 400, ErrorCodes.InvalidOtp,
                    logReason: "Parent reset: invalid or no active OTP.")
            };
        }

        string passwordHash = _passwordHasher.Hash(request.NewPassword);
        await _parents.SetPasswordAsync(parentId.Value, passwordHash, cancellationToken);
        await _refreshTokenService.RevokeAllForActorAsync(AuthActorTypes.Parent, parentId.Value, cancellationToken);
    }

    public async Task<ParentMeResponse> GetMeAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_requestContext.UserId, out Guid parentId))
        {
            throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
        }

        ParentProfileRow profile = await _parents.GetProfileAsync(parentId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);

        return new ParentMeResponse
        {
            FullName = profile.FullName,
            Phone = profile.Phone,
            Email = profile.Email,
            PhoneVerified = profile.PhoneVerified,
            HasPaymentPin = profile.HasPaymentPin
        };
    }

    private async Task<ParentTokensResult> IssueTokensAsync(Guid parentId, string phone,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        AccessToken access = _accessTokenIssuer.IssueParent(parentId, phone);

        RefreshTokenIssue refresh = await _refreshTokenService.IssueAsync(
            AuthActorTypes.Parent, parentId, ipAddress, userAgent, cancellationToken);

        return new ParentTokensResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = refresh.Token,
            RefreshTokenExpiresAt = refresh.ExpiresAt
        };
    }

    private async Task ApplyFailedLoginAsync(ParentLoginRow parent, string phone, CancellationToken cancellationToken)
    {
        int newCount = parent.FailedLoginCount + 1;
        DateTime? lockedUntil = null;

        if (newCount >= MaxFailedLogins)
        {
            lockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            newCount = 0;
        }

        await _parents.SetLoginFailureAsync(parent.Id, newCount, lockedUntil, cancellationToken);

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
            "parents_phone_key" => new AppErrorException(RegistrationFailed, 409, ErrorCodes.PhoneTaken,
                logReason: "Unique violation: parents_phone_key"),
            "parents_email_key" => new AppErrorException(RegistrationFailed, 409, ErrorCodes.EmailTaken,
                logReason: "Unique violation: parents_email_key"),
            _ => new AppErrorException(RegistrationFailed, 409, ErrorCodes.Conflict,
                logReason: $"Unique violation: {ex.ConstraintName}")
        };
    }
}
