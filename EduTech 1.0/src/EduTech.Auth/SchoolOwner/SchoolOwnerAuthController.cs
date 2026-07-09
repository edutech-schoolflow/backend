using EduTech.Shared.Features;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduTech.Auth.SchoolOwner;

/// <summary>
/// School Owner (Actor 1) authentication endpoints. Tokens are returned as httpOnly cookies
/// (Cross-Cutting Auth §X.2); validation/auth errors bubble to GlobalExceptionMiddleware.
/// </summary>
[ApiController]
[Route("api/v1/school/auth")]
public sealed class SchoolOwnerAuthController : ControllerBase
{
    // Legacy portal auth removed (EDD-001 Sprint 5) — register/login/password flows now live at /api/v1/auth.
    private const string AccessCookie = "sf_access";
    private const string RefreshCookie = "sf_refresh";

    private readonly ISchoolOwnerAuthService _authService;

    public SchoolOwnerAuthController(ISchoolOwnerAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    [EnableRateLimiting("otp")]
    [SignupGate("school_owner")]
    public async Task<ActionResult<ServiceResponses<string?>>> Register(
        [FromBody] RegisterSchoolOwnerRequest request, CancellationToken cancellationToken)
    {
        await _authService.RegisterAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null,
            "Account created. We sent a verification code to your phone."));
    }

    [HttpPost("verify-phone")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<string?>>> VerifyPhone(
        [FromBody] VerifyPhoneRequest request, CancellationToken cancellationToken)
    {
        await _authService.VerifyPhoneAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Phone verified. You can now log in."));
    }


    [HttpPost("refresh")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<LoginResponse>>> Refresh(CancellationToken cancellationToken)
    {
        string refreshToken = Request.Cookies[RefreshCookie] ?? string.Empty;
        LoginResult result = await _authService.RefreshAsync(refreshToken, ClientIp(), UserAgent(), cancellationToken);
        SetAuthCookies(result);
        return Ok(ServiceResponses<LoginResponse>.Ok(
            new LoginResponse { AccessTokenExpiresAt = result.AccessTokenExpiresAt }, "Token refreshed."));
    }

    [HttpPost("resend-otp")]
    [EnableRateLimiting("otp")]
    public async Task<ActionResult<ServiceResponses<string?>>> ResendOtp(
        [FromBody] ResendOtpRequest request, CancellationToken cancellationToken)
    {
        await _authService.ResendOtpAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "If your number needs verifying, we sent a new code."));
    }



    [HttpGet("me")]
    [Authorize(Policy = "SchoolOnly")]
    public async Task<ActionResult<ServiceResponses<SchoolOwnerMeResponse>>> Me(CancellationToken cancellationToken)
    {
        SchoolOwnerMeResponse me = await _authService.GetMeAsync(cancellationToken);
        return Ok(ServiceResponses<SchoolOwnerMeResponse>.Ok(me, "Profile."));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private void SetAuthCookies(LoginResult result)
    {
        // Secure only over HTTPS so cookies still set on http://localhost during dev.
        // Cross-origin SPA integration may later require SameSite=None; Secure.
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
