using EduTech.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Auth.Unified;

/// <summary>
/// EDD-001 Sprint 2 — the single registration and login for every person. Tokens travel as the same
/// httpOnly cookies the portals already read (<c>sf_access</c>/<c>sf_refresh</c>), so once a context
/// is entered the existing portal middleware, refresh endpoints and logout all apply unchanged.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class UnifiedAuthController : ControllerBase
{
    private const string AccessCookie = "sf_access";
    private const string RefreshCookie = "sf_refresh";

    private readonly IUnifiedAuthService _service;

    public UnifiedAuthController(IUnifiedAuthService service)
    {
        _service = service;
    }

    /// <summary>One registration for everyone — creates an Identity; roles come later.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ServiceResponses<string?>>> Register(
        [FromBody] UnifiedRegisterRequest request, CancellationToken cancellationToken)
    {
        await _service.RegisterAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null,
            "Account created. Enter the code we sent to your phone to verify it."));
    }

    [HttpPost("verify-phone")]
    [AllowAnonymous]
    public async Task<ActionResult<ServiceResponses<string?>>> VerifyPhone(
        [FromBody] UnifiedVerifyPhoneRequest request, CancellationToken cancellationToken)
    {
        await _service.VerifyPhoneAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Phone verified. You can now log in."));
    }

    [HttpPost("resend-otp")]
    [AllowAnonymous]
    public async Task<ActionResult<ServiceResponses<string?>>> ResendOtp(
        [FromBody] UnifiedResendOtpRequest request, CancellationToken cancellationToken)
    {
        await _service.ResendOtpAsync(request.Phone, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "If that number is registered, a code is on its way."));
    }

    /// <summary>
    /// One login. Single context → cookies are set and <c>selected</c> names it. Several → the body
    /// lists them; call again with <c>contextKey</c>. None → empty list (welcome flow).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ServiceResponses<UnifiedLoginResponse>>> Login(
        [FromBody] UnifiedLoginRequest request, CancellationToken cancellationToken)
    {
        UnifiedLoginResult result = await _service.LoginAsync(request, ClientIp(),
            Request.Headers.UserAgent.FirstOrDefault(), cancellationToken);

        if (result.Tokens is UnifiedTokens tokens)
        {
            SetAuthCookies(tokens);
        }

        string message = result.Selected is not null
            ? "Welcome back."
            : result.Contexts.Count == 0
                ? "You're not linked to any school yet."
                : "Choose which organization to enter.";

        return Ok(ServiceResponses<UnifiedLoginResponse>.Ok(
            new UnifiedLoginResponse { Contexts = result.Contexts, Selected = result.Selected }, message));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ServiceResponses<string?>>> ForgotPassword(
        [FromBody] UnifiedForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _service.ForgotPasswordAsync(request.Phone, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "If that number is registered, a reset code is on its way."));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ServiceResponses<string?>>> ResetPassword(
        [FromBody] UnifiedResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _service.ResetPasswordAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Password updated. You can now log in."));
    }


    /// <summary>
    /// EDD-005 Principle 6 — the ONE refresh for every session kind. The refresh cookie's session
    /// record decides what gets minted (identity/parent/staff/owner); the browser URL plays no part.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<string?>>> Refresh(CancellationToken cancellationToken)
    {
        string refreshToken = Request.Cookies[RefreshCookie] ?? string.Empty;
        UnifiedTokens tokens = await _service.RefreshSessionAsync(refreshToken, ClientIp(),
            Request.Headers.UserAgent.FirstOrDefault(), cancellationToken);
        SetAuthCookies(tokens);
        return Ok(ServiceResponses<string?>.Ok(null, "Session refreshed."));
    }


    [HttpPost("select-context")]
    [Authorize(Policy = "AuthenticatedIdentity")]
    public async Task<ActionResult<ServiceResponses<UnifiedLoginResponse>>> SelectContext(
        [FromBody] SelectContextRequest request, CancellationToken cancellationToken)
    {
        string? userType = User.FindFirst("user_type")?.Value;
        string? sub = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
        if (userType is null || !Guid.TryParse(sub, out Guid actorId))
        {
            return Unauthorized();
        }

        UnifiedLoginResult result = await _service.SelectContextAsync(userType, actorId, request.ContextId,
            ClientIp(), Request.Headers.UserAgent.FirstOrDefault(), cancellationToken);
        SetAuthCookies(result.Tokens!);

        return Ok(ServiceResponses<UnifiedLoginResponse>.Ok(
            new UnifiedLoginResponse { Contexts = result.Contexts, Selected = result.Selected },
            "Welcome back."));
    }


    private void SetAuthCookies(UnifiedTokens tokens)
    {
        bool secure = Request.IsHttps;

        Response.Cookies.Append(AccessCookie, tokens.AccessToken, new CookieOptions
        {
            HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Lax,
            Expires = tokens.AccessTokenExpiresAt, Path = "/"
        });

        Response.Cookies.Append(RefreshCookie, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Lax,
            Expires = tokens.RefreshTokenExpiresAt, Path = "/"
        });
    }

    private string? ClientIp()
    {
        return Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
