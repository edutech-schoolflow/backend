using EduTech.Shared.Features;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduTech.Auth.Staff;

/// <summary>
/// Standalone Staff (Actor 2) authentication endpoints. Identity-only login (no school yet).
/// Tokens are returned as httpOnly cookies; errors bubble to GlobalExceptionMiddleware.
/// </summary>
[ApiController]
[Route("api/v1/staff/auth")]
public sealed class StaffAuthController : ControllerBase
{
    private const string AccessCookie = "sf_access";
    private const string RefreshCookie = "sf_refresh";

    private readonly IStaffAuthService _authService;

    public StaffAuthController(IStaffAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    [EnableRateLimiting("otp")]
    [SignupGate("staff")]
    public async Task<ActionResult<ServiceResponses<string?>>> Register(
        [FromBody] RegisterStaffRequest request, CancellationToken cancellationToken)
    {
        await _authService.RegisterAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null,
            "Account created. We sent a verification code to your phone."));
    }

    [HttpPost("verify-phone")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<string?>>> VerifyPhone(
        [FromBody] StaffVerifyPhoneRequest request, CancellationToken cancellationToken)
    {
        await _authService.VerifyPhoneAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Phone verified. You can now log in."));
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [MaintenanceGate]
    public async Task<ActionResult<ServiceResponses<StaffAuthResponse>>> Login(
        [FromBody] StaffLoginRequest request, CancellationToken cancellationToken)
    {
        StaffTokensResult result = await _authService.LoginAsync(request, ClientIp(), UserAgent(), cancellationToken);
        SetAuthCookies(result);
        return Ok(ServiceResponses<StaffAuthResponse>.Ok(
            new StaffAuthResponse { AccessTokenExpiresAt = result.AccessTokenExpiresAt }, "Logged in."));
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<StaffAuthResponse>>> Refresh(CancellationToken cancellationToken)
    {
        string refreshToken = Request.Cookies[RefreshCookie] ?? string.Empty;
        StaffTokensResult result = await _authService.RefreshAsync(refreshToken, ClientIp(), UserAgent(), cancellationToken);
        SetAuthCookies(result);
        return Ok(ServiceResponses<StaffAuthResponse>.Ok(
            new StaffAuthResponse { AccessTokenExpiresAt = result.AccessTokenExpiresAt }, "Token refreshed."));
    }

    [HttpPost("resend-otp")]
    [EnableRateLimiting("otp")]
    public async Task<ActionResult<ServiceResponses<string?>>> ResendOtp(
        [FromBody] StaffResendOtpRequest request, CancellationToken cancellationToken)
    {
        await _authService.ResendOtpAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "If your number needs verifying, we sent a new code."));
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<string?>>> ForgotPassword(
        [FromBody] StaffForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _authService.ForgotPasswordAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "If that account exists, we sent a reset code."));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<string?>>> ResetPassword(
        [FromBody] StaffResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _authService.ResetPasswordAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Password reset. Please log in."));
    }

    [HttpGet("me")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<ActionResult<ServiceResponses<StaffMeResponse>>> Me(CancellationToken cancellationToken)
    {
        StaffMeResponse me = await _authService.GetMeAsync(cancellationToken);
        return Ok(ServiceResponses<StaffMeResponse>.Ok(me, "Profile."));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private void SetAuthCookies(StaffTokensResult result)
    {
        bool secure = Request.IsHttps;

        Response.Cookies.Append(AccessCookie, result.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Expires = result.AccessTokenExpiresAt,
            Path = "/"
        });

        Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Expires = result.RefreshTokenExpiresAt,
            Path = "/"
        });
    }

    private string? ClientIp()
    {
        return Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private string? UserAgent()
    {
        return Request.Headers.UserAgent.ToString();
    }
}
