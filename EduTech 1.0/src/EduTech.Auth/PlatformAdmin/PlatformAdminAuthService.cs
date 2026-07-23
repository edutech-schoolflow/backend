using EduTech.Auth.RefreshTokens;
using EduTech.Auth.Security;
using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.PlatformAdmin;

/// <summary>Platform admin auth (Actor 4): seed first super_admin, then email + password login.</summary>
public interface IPlatformAdminAuthService
{
    /// <summary>Creates the first super_admin. Only succeeds while no admins exist (dev seed).</summary>
    Task SeedSuperAdminAsync(SeedAdminRequest request, CancellationToken cancellationToken);

    Task<AdminTokensResult> LoginAsync(AdminLoginRequest request, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken);
}

internal sealed class PlatformAdminAuthService : IPlatformAdminAuthService
{
    private const int MinPasswordLength = 8;
    private const int MaxFailedLogins = 5;
    private const int LockoutMinutes = 15;

    private readonly IPlatformAdminRepository _admins;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAccessTokenIssuer _accessTokenIssuer;
    private readonly IRefreshTokenService _refreshTokenService;

    public PlatformAdminAuthService(
        IPlatformAdminRepository admins,
        IPasswordHasher passwordHasher,
        IAccessTokenIssuer accessTokenIssuer,
        IRefreshTokenService refreshTokenService)
    {
        _admins = admins;
        _passwordHasher = passwordHasher;
        _accessTokenIssuer = accessTokenIssuer;
        _refreshTokenService = refreshTokenService;
    }

    public async Task SeedSuperAdminAsync(SeedAdminRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new AppErrorException("First and last name are required.", 400, ErrorCodes.ValidationError);
        }

        string email = NormalizeEmail(request.Email);
        if (email.Length == 0)
        {
            throw new AppErrorException("A valid email is required.", 400, ErrorCodes.ValidationError);
        }

        if (request.Password.Length < MinPasswordLength)
        {
            throw new AppErrorException($"Password must be at least {MinPasswordLength} characters.",
                400, ErrorCodes.ValidationError);
        }

        if (await _admins.ExistsAnyAsync(cancellationToken))
        {
            throw new AppErrorException("Platform admins already exist; seeding is disabled.",
                409, ErrorCodes.Conflict);
        }

        string passwordHash = _passwordHasher.Hash(request.Password);
        await _admins.CreateAsync(request.FirstName.Trim(),
            string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
            request.LastName.Trim(), email, passwordHash, PlatformAdminRoles.SuperAdmin, null, cancellationToken);
    }

    public async Task<AdminTokensResult> LoginAsync(AdminLoginRequest request, string? ipAddress,
        string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new AppErrorException("Email and password are required.", 400, ErrorCodes.ValidationError);
        }

        string email = NormalizeEmail(request.Email);
        PlatformAdminLoginRow? admin = await _admins.GetByEmailForLoginAsync(email, cancellationToken);

        if (admin is null)
        {
            throw new AppErrorException("Invalid email or password.", 401, ErrorCodes.Unauthorized,
                logReason: "Admin login: no account for email.");
        }

        if (!admin.IsActive)
        {
            throw new AppErrorException("This account is inactive.", 403, ErrorCodes.AccountInactive,
                logReason: "Admin login: account inactive.");
        }

        if (admin.LockedUntil is not null && admin.LockedUntil > DateTime.UtcNow)
        {
            throw new AppErrorException("Account locked after failed attempts. Try again later.",
                429, ErrorCodes.AccountLocked, logReason: "Admin login: account locked.");
        }

        if (!_passwordHasher.Verify(request.Password, admin.PasswordHash))
        {
            await ApplyFailedLoginAsync(admin, cancellationToken);
            throw new AppErrorException("Invalid email or password.", 401, ErrorCodes.Unauthorized,
                logReason: "Admin login: wrong password.");
        }

        await _admins.ClearLoginFailuresAsync(admin.Id, cancellationToken);

        AccessToken access = _accessTokenIssuer.IssuePlatformAdmin(admin.Id, admin.Role, admin.Email);
        RefreshTokenIssue refresh = await _refreshTokenService.IssueAsync(
            AuthActorTypes.PlatformAdmin, admin.Id, identityId: null, contextId: null, ipAddress, userAgent, cancellationToken);

        return new AdminTokensResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = refresh.Token,
            RefreshTokenExpiresAt = refresh.ExpiresAt
        };
    }

    private async Task ApplyFailedLoginAsync(PlatformAdminLoginRow admin, CancellationToken cancellationToken)
    {
        int newCount = admin.FailedLoginCount + 1;
        DateTime? lockedUntil = null;

        if (newCount >= MaxFailedLogins)
        {
            lockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            newCount = 0;
        }

        await _admins.SetLoginFailureAsync(admin.Id, newCount, lockedUntil, cancellationToken);
    }

    private static string NormalizeEmail(string? email)
    {
        return (email ?? string.Empty).Trim().ToLowerInvariant();
    }
}
