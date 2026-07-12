using EduTech.Shared.Features;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduTech.Auth.Parent;

/// <summary>
/// Parent (Actor 3) authentication. Standalone, phone-first. Tokens via httpOnly cookies.
/// </summary>
[ApiController]
[Route("api/v1/parent/auth")]
public sealed class ParentAuthController : ControllerBase
{
    // Legacy portal auth removed (EDD-001 Sprint 5) — register/login/password flows now live at /api/v1/auth.
    private const string AccessCookie = "sf_access";
    private const string RefreshCookie = "sf_refresh";

    private readonly IParentAuthService _authService;

    public ParentAuthController(IParentAuthService authService)
    {
        _authService = authService;
    }




    [HttpPost("payment-pin")]
    [Authorize(Policy = "ParentOnly")]
    public async Task<ActionResult<ServiceResponses<string?>>> SetPaymentPin(
        [FromBody] SetPaymentPinRequest request, CancellationToken cancellationToken)
    {
        await _authService.SetPaymentPinAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Payment PIN set."));
    }




    [HttpGet("me")]
    [Authorize(Policy = "ParentOnly")]
    public async Task<ActionResult<ServiceResponses<ParentMeResponse>>> Me(CancellationToken cancellationToken)
    {
        ParentMeResponse me = await _authService.GetMeAsync(cancellationToken);
        return Ok(ServiceResponses<ParentMeResponse>.Ok(me, "Profile."));
    }

    private void SetAuthCookies(ParentTokensResult result)
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
