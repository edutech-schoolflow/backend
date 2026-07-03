using EduTech.Auth.Otp;
using EduTech.Auth.RefreshTokens;
using EduTech.Auth.Security;
using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using EduTech.Shared.Persistence;
using EduTech.Shared.Phone;
using Npgsql;

namespace EduTech.Auth.SchoolOwner;

internal sealed class SchoolOwnerAuthService : ISchoolOwnerAuthService
{
    private const int MinPasswordLength = 8;
    private const int MaxFailedLogins = 5;
    private const int LockoutMinutes = 15;

    // Deliberately vague client message for duplicate/registration failures (anti-enumeration);
    // the real reason goes only to the logs via AppErrorException.LogReason.
    private const string RegistrationFailed = "Registration failed. Something went wrong.";

    private readonly IEduTechRequestContext _requestContext;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ISchoolRepository _schoolRepository;
    private readonly ISchoolOwnerRepository _ownerRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IOtpService _otpService;
    private readonly INotificationDispatcher _notifications;
    private readonly IAccessTokenIssuer _accessTokenIssuer;
    private readonly IRefreshTokenService _refreshTokenService;

    public SchoolOwnerAuthService(
        IEduTechRequestContext requestContext,
        IDbConnectionFactory connectionFactory,
        ISchoolRepository schoolRepository,
        ISchoolOwnerRepository ownerRepository,
        IPasswordHasher passwordHasher,
        IOtpService otpService,
        INotificationDispatcher notifications,
        IAccessTokenIssuer accessTokenIssuer,
        IRefreshTokenService refreshTokenService)
    {
        _requestContext = requestContext;
        _connectionFactory = connectionFactory;
        _schoolRepository = schoolRepository;
        _ownerRepository = ownerRepository;
        _passwordHasher = passwordHasher;
        _otpService = otpService;
        _notifications = notifications;
        _accessTokenIssuer = accessTokenIssuer;
        _refreshTokenService = refreshTokenService;
    }

    public async Task RegisterAsync(RegisterSchoolOwnerRequest request, CancellationToken cancellationToken)
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

        // Friendly pre-checks (the unique constraints below are the real guard against races).
        if (await _ownerRepository.ExistsByPhoneAsync(phone, cancellationToken))
        {
            throw new AppErrorException(RegistrationFailed, 409, ErrorCodes.PhoneTaken,
                logReason: "Registration blocked: phone already registered.");
        }

        if (email is not null && await _ownerRepository.ExistsByEmailAsync(email, cancellationToken))
        {
            throw new AppErrorException(RegistrationFailed, 409, ErrorCodes.EmailTaken,
                logReason: "Registration blocked: email already in use.");
        }

        string passwordHash = _passwordHasher.Hash(request.Password);

        // Atomic: school shell + owner are created together, or not at all.
        Guid ownerId;
        try
        {
            await using (DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken))
            {
                Guid schoolId = await _schoolRepository.CreateShellAsync(transaction.Transaction, cancellationToken);
                ownerId = await _ownerRepository.CreateAsync(schoolId, request.FirstName.Trim(),
                    string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
                    request.LastName.Trim(), phone, email, passwordHash, transaction.Transaction, cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Backstop: covers the race window the pre-checks can't (two registrations at once).
            throw MapUniqueViolation(ex);
        }

        // After commit — if OTP/SMS fail here, the account still exists and the owner can request a
        // resend, so we deliberately keep this out of the transaction.
        string code = await _otpService.GenerateAsync(
            OtpPurpose.SchoolOwnerPhoneVerification, ownerId, phone, cancellationToken);

        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow verification code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task VerifyPhoneAsync(VerifyPhoneRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new AppErrorException("Phone and code are required.", 400, ErrorCodes.ValidationError);
        }

        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            throw new AppErrorException("Invalid verification request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Verify-phone: phone failed normalization.");
        }

        Guid? ownerId = await _ownerRepository.GetIdByPhoneAsync(phone, cancellationToken);
        if (ownerId is null)
        {
            // Public message is vague (don't reveal whether the phone is registered); the log knows.
            throw new AppErrorException("Invalid verification request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Verify-phone: no account for this phone.");
        }

        OtpVerifyResult result = await _otpService.VerifyAsync(
            OtpPurpose.SchoolOwnerPhoneVerification, ownerId.Value, request.Code.Trim(), cancellationToken);

        switch (result)
        {
            case OtpVerifyResult.Success:
                await _ownerRepository.MarkPhoneVerifiedAsync(ownerId.Value, cancellationToken);
                return;

            case OtpVerifyResult.Expired:
                throw new AppErrorException("Verification code has expired. Request a new one.",
                    400, ErrorCodes.OtpExpired, logReason: "Verify-phone: OTP expired.");

            case OtpVerifyResult.TooManyAttempts:
                throw new AppErrorException("Too many incorrect attempts. Request a new code.",
                    429, ErrorCodes.TooManyRequests, logReason: "Verify-phone: too many OTP attempts.");

            case OtpVerifyResult.NotFound:
                throw new AppErrorException("Invalid verification code.", 400, ErrorCodes.InvalidOtp,
                    logReason: "Verify-phone: no active OTP (not requested or already used).");

            default: // Invalid
                throw new AppErrorException("Invalid verification code.", 400, ErrorCodes.InvalidOtp,
                    logReason: "Verify-phone: wrong code entered.");
        }
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new AppErrorException("Phone and password are required.", 400, ErrorCodes.ValidationError);
        }

        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            // Can't match any account → same uniform error as a wrong password.
            throw new AppErrorException("Invalid phone or password.", 401, ErrorCodes.Unauthorized,
                logReason: "Login: phone failed normalization.");
        }

        SchoolOwnerLoginRow? owner = await _ownerRepository.GetByPhoneForLoginAsync(phone, cancellationToken);

        // Uniform error so attackers can't tell which phones are registered.
        if (owner is null)
        {
            throw new AppErrorException("Invalid phone or password.", 401, ErrorCodes.Unauthorized,
                logReason: "Login: no account for this phone.");
        }

        if (!owner.IsActive)
        {
            throw new AppErrorException("This account is inactive.", 403, ErrorCodes.AccountInactive,
                logReason: "Login: account inactive.");
        }

        if (owner.LockedUntil is not null && owner.LockedUntil > DateTime.UtcNow)
        {
            throw new AppErrorException("Account locked after failed attempts. Try again later.",
                429, ErrorCodes.AccountLocked, logReason: "Login: account locked.");
        }

        if (!_passwordHasher.Verify(request.Password, owner.PasswordHash))
        {
            await ApplyFailedLoginAsync(owner, phone, cancellationToken);
            throw new AppErrorException("Invalid phone or password.", 401, ErrorCodes.Unauthorized,
                logReason: "Login: wrong password.");
        }

        // Only a caller with the correct password learns the phone is unverified (no enumeration leak).
        if (!owner.PhoneVerified)
        { 
            throw new AppErrorException("Please verify your phone number before logging in.",
                403, ErrorCodes.PhoneNotVerified, logReason: "Login: phone not verified.");
        }

        await _ownerRepository.ClearLoginFailuresAsync(owner.Id, cancellationToken);

        SchoolStatusRow status = await _schoolRepository.GetStatusAsync(owner.SchoolId, cancellationToken)
            ?? throw new AppErrorException("School not found.", 404, ErrorCodes.NotFound);

        AccessToken access = _accessTokenIssuer.IssueSchoolOwner(
            owner.Id, owner.SchoolId, phone, status.Status, status.KycStatus, status.Subdomain);

        RefreshTokenIssue refresh = await _refreshTokenService.IssueAsync(
            AuthActorTypes.SchoolOwner, owner.Id, ipAddress, userAgent, cancellationToken);

        return new LoginResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = refresh.Token,
            RefreshTokenExpiresAt = refresh.ExpiresAt
        };
    }

    public async Task<LoginResult> RefreshAsync(string refreshToken, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AppErrorException("Missing refresh token.", 401, ErrorCodes.Unauthorized,
                logReason: "Refresh: no refresh cookie present.");
        }

        RefreshRotationResult rotation = await _refreshTokenService.RotateAsync(
            refreshToken, ipAddress, userAgent, cancellationToken);

        if (!rotation.IsSuccess || rotation.ActorType != AuthActorTypes.SchoolOwner)
        {
            throw new AppErrorException("Session expired. Please log in again.", 401, ErrorCodes.Unauthorized,
                logReason: $"Refresh: rotation failed ({rotation.Status}).");
        }

        SchoolOwnerTokenRow? owner = await _ownerRepository.GetTokenClaimsAsync(rotation.ActorId, cancellationToken);
        if (owner is null || !owner.IsActive)
        {
            // Account removed or deactivated since the token was issued — revoke and force re-login.
            await _refreshTokenService.RevokeAllForActorAsync(
                AuthActorTypes.SchoolOwner, rotation.ActorId, cancellationToken);
            throw new AppErrorException("Session is no longer valid. Please log in again.",
                401, ErrorCodes.Unauthorized, logReason: "Refresh: owner missing or deactivated.");
        }

        SchoolStatusRow status = await _schoolRepository.GetStatusAsync(owner.SchoolId, cancellationToken)
            ?? throw new AppErrorException("School not found.", 404, ErrorCodes.NotFound);

        AccessToken access = _accessTokenIssuer.IssueSchoolOwner(
            rotation.ActorId, owner.SchoolId, owner.Phone, status.Status, status.KycStatus, status.Subdomain);

        return new LoginResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = rotation.NewToken!,
            RefreshTokenExpiresAt = rotation.ExpiresAt
        };
    }

    public async Task ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            return; // uniform response — never reveal whether a phone is registered
        }

        SchoolOwnerLoginRow? owner = await _ownerRepository.GetByPhoneForLoginAsync(phone, cancellationToken);
        if (owner is null || owner.PhoneVerified)
        {
            return; // unknown phone or already verified → no-op
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.SchoolOwnerPhoneVerification, owner.Id, phone, cancellationToken);
        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow verification code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            return; // always report success to the caller (anti-enumeration)
        }

        Guid? ownerId = await _ownerRepository.GetIdByPhoneAsync(phone, cancellationToken);
        if (ownerId is null)
        {
            return;
        }

        string code = await _otpService.GenerateAsync(
            OtpPurpose.SchoolOwnerPasswordReset, ownerId.Value, phone, cancellationToken);
        await _notifications.SendSmsAsync(phone,
            $"Your SchoolFlow password reset code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
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
                logReason: "Reset: phone failed normalization.");
        }

        Guid? ownerId = await _ownerRepository.GetIdByPhoneAsync(phone, cancellationToken);
        if (ownerId is null)
        {
            throw new AppErrorException("Invalid reset request.", 400, ErrorCodes.InvalidOtp,
                logReason: "Reset: no account for this phone.");
        }

        OtpVerifyResult result = await _otpService.VerifyAsync(
            OtpPurpose.SchoolOwnerPasswordReset, ownerId.Value, request.Code.Trim(), cancellationToken);
        if (result != OtpVerifyResult.Success)
        {
            throw result switch
            {
                OtpVerifyResult.Expired => new AppErrorException("Reset code has expired. Request a new one.",
                    400, ErrorCodes.OtpExpired, logReason: "Reset: OTP expired."),
                OtpVerifyResult.TooManyAttempts => new AppErrorException("Too many incorrect attempts. Request a new code.",
                    429, ErrorCodes.TooManyRequests, logReason: "Reset: too many OTP attempts."),
                _ => new AppErrorException("Invalid reset code.", 400, ErrorCodes.InvalidOtp,
                    logReason: "Reset: invalid or no active OTP.")
            };
        }

        string passwordHash = _passwordHasher.Hash(request.NewPassword);
        await _ownerRepository.SetPasswordAsync(ownerId.Value, passwordHash, cancellationToken);

        // Force re-login everywhere after a password change.
        await _refreshTokenService.RevokeAllForActorAsync(AuthActorTypes.SchoolOwner, ownerId.Value, cancellationToken);
    }

    public async Task<SchoolOwnerMeResponse> GetMeAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_requestContext.UserId, out Guid ownerId))
        {
            throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
        }

        SchoolOwnerProfileRow profile = await _ownerRepository.GetProfileAsync(ownerId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);

        return new SchoolOwnerMeResponse
        {
            FullName = profile.FullName,
            Phone = profile.Phone,
            Email = profile.Email,
            PhoneVerified = profile.PhoneVerified,
            SchoolId = profile.SchoolId,
            SchoolStatus = profile.SchoolStatus,
            KycStatus = profile.KycStatus,
            Subdomain = profile.Subdomain
        };
    }

    private async Task ApplyFailedLoginAsync(SchoolOwnerLoginRow owner, string phone,
        CancellationToken cancellationToken)
    {
        int newCount = owner.FailedLoginCount + 1;
        DateTime? lockedUntil = null;

        if (newCount >= MaxFailedLogins)
        {
            lockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            newCount = 0;   // counter resets; the lock window is now the penalty
        }

        await _ownerRepository.SetLoginFailureAsync(owner.Id, newCount, lockedUntil, cancellationToken);

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
            "school_owners_phone_key" => new AppErrorException(RegistrationFailed, 409, ErrorCodes.PhoneTaken,
                logReason: "Unique violation: school_owners_phone_key"),
            "school_owners_email_key" => new AppErrorException(RegistrationFailed, 409, ErrorCodes.EmailTaken,
                logReason: "Unique violation: school_owners_email_key"),
            _ => new AppErrorException(RegistrationFailed, 409, ErrorCodes.Conflict,
                logReason: $"Unique violation: {ex.ConstraintName}")
        };
    }
}

