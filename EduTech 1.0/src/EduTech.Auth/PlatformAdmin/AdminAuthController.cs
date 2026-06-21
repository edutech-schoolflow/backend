using EduTech.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;

namespace EduTech.Auth.PlatformAdmin;

/// <summary>Platform Admin (Actor 4) auth. Tokens via httpOnly cookies. (TOTP is a planned follow-up.)</summary>
[ApiController]
[Route("api/v1/admin/auth")]
public sealed class AdminAuthController : ControllerBase
{
    private const string AccessCookie = "sf_access";
    private const string RefreshCookie = "sf_refresh";

    private readonly IPlatformAdminAuthService _authService;
    private readonly IWebHostEnvironment _environment;

    public AdminAuthController(IPlatformAdminAuthService authService, IWebHostEnvironment environment)
    {
        _authService = authService;
        _environment = environment;
    }

    /// <summary>Dev-only: create the first super_admin (only works while none exist).</summary>
    [HttpPost("seed")]
    public async Task<ActionResult<ServiceResponses<string?>>> Seed(
        [FromBody] SeedAdminRequest request, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        await _authService.SeedSuperAdminAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Super admin created."));
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<AdminAuthResponse>>> Login(
        [FromBody] AdminLoginRequest request, CancellationToken cancellationToken)
    {
        AdminTokensResult result = await _authService.LoginAsync(request, ClientIp(), UserAgent(), cancellationToken);
        SetAuthCookies(result);
        return Ok(ServiceResponses<AdminAuthResponse>.Ok(
            new AdminAuthResponse { AccessTokenExpiresAt = result.AccessTokenExpiresAt }, "Logged in."));
    }

    private void SetAuthCookies(AdminTokensResult result)
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
