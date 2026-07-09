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
    // Legacy portal auth removed (EDD-001 Sprint 5) — register/login/password flows now live at /api/v1/auth.
    private const string AccessCookie = "sf_access";
    private const string RefreshCookie = "sf_refresh";

    private readonly IStaffAuthService _authService;

    public StaffAuthController(IStaffAuthService authService)
    {
        _authService = authService;
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
