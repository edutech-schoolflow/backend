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
    private const string AccessCookie = "sf_access";
    private const string RefreshCookie = "sf_refresh";

    private readonly IParentAuthService _authService;

    public ParentAuthController(IParentAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    [EnableRateLimiting("otp")]
    [SignupGate("parent")]
    public async Task<ActionResult<ServiceResponses<string?>>> Register(
        [FromBody] RegisterParentRequest request, CancellationToken cancellationToken)
    {
        await _authService.RegisterAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null,
            "Account created. We sent a verification code to your phone."));
    }

    [HttpPost("verify-phone")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<string?>>> VerifyPhone(
        [FromBody] ParentVerifyPhoneRequest request, CancellationToken cancellationToken)
    {
        await _authService.VerifyPhoneAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Phone verified. You can now log in."));
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [MaintenanceGate]
    public async Task<ActionResult<ServiceResponses<ParentAuthResponse>>> Login(
        [FromBody] ParentLoginRequest request, CancellationToken cancellationToken)
    {
        ParentTokensResult result = await _authService.LoginAsync(request, ClientIp(), UserAgent(), cancellationToken);
        SetAuthCookies(result);
        return Ok(ServiceResponses<ParentAuthResponse>.Ok(
            new ParentAuthResponse { AccessTokenExpiresAt = result.AccessTokenExpiresAt }, "Logged in."));
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<ParentAuthResponse>>> Refresh(CancellationToken cancellationToken)
    {
        string refreshToken = Request.Cookies[RefreshCookie] ?? string.Empty;
        ParentTokensResult result = await _authService.RefreshAsync(refreshToken, ClientIp(), UserAgent(), cancellationToken);
        SetAuthCookies(result);
        return Ok(ServiceResponses<ParentAuthResponse>.Ok(
            new ParentAuthResponse { AccessTokenExpiresAt = result.AccessTokenExpiresAt }, "Token refreshed."));
    }

    [HttpPost("payment-pin")]
    [Authorize(Policy = "ParentOnly")]
    public async Task<ActionResult<ServiceResponses<string?>>> SetPaymentPin(
        [FromBody] SetPaymentPinRequest request, CancellationToken cancellationToken)
    {
        await _authService.SetPaymentPinAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Payment PIN set."));
    }

    [HttpPost("resend-otp")]
    [EnableRateLimiting("otp")]
    public async Task<ActionResult<ServiceResponses<string?>>> ResendOtp(
        [FromBody] ParentResendOtpRequest request, CancellationToken cancellationToken)
    {
        await _authService.ResendOtpAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "If your number needs verifying, we sent a new code."));
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<string?>>> ForgotPassword(
        [FromBody] ParentForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _authService.ForgotPasswordAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "If that account exists, we sent a reset code."));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<string?>>> ResetPassword(
        [FromBody] ParentResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _authService.ResetPasswordAsync(request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Password reset. Please log in."));
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
