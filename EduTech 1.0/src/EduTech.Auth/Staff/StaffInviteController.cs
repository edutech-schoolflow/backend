using EduTech.Workforce;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduTech.Auth.Staff;

/// <summary>
/// Staff-side invite acceptance (Actor 2, Path B). Public endpoints: a new staff member has no
/// session yet. The accept endpoint OPTIONALLY reads an existing StaffAuth session (for the
/// existing-account branch) without requiring it.
/// </summary>
[ApiController]
[Route("api/v1/staff/invite")]
public sealed class StaffInviteController : ControllerBase
{
    private const string AccessCookie = "sf_access";
    private const string RefreshCookie = "sf_refresh";

    private readonly IStaffInviteAcceptService _acceptService;

    public StaffInviteController(IStaffInviteAcceptService acceptService)
    {
        _acceptService = acceptService;
    }

    [HttpGet("validate")]
    public async Task<ActionResult<ServiceResponses<InviteDetailsResponse>>> Validate(
        [FromQuery] string token, CancellationToken cancellationToken)
    {
        InviteDetailsResponse details = await _acceptService.ValidateAsync(token, cancellationToken);
        return Ok(ServiceResponses<InviteDetailsResponse>.Ok(details, "Invitation is valid."));
    }

    [HttpPost("send-otp")]
    [EnableRateLimiting("otp")]
    public async Task<ActionResult<ServiceResponses<string?>>> SendOtp(
        [FromBody] InviteTokenRequest request, CancellationToken cancellationToken)
    {
        await _acceptService.SendOtpAsync(request.Token, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "We sent a verification code to your phone."));
    }

    [HttpPost("accept")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ServiceResponses<StaffAuthResponse>>> Accept(
        [FromBody] AcceptInviteRequest request, CancellationToken cancellationToken)
    {
        Guid? authenticatedStaffUserId = await TryGetAuthenticatedStaffIdAsync();

        StaffTokensResult result = await _acceptService.AcceptAsync(
            request, authenticatedStaffUserId, ClientIp(), UserAgent(), cancellationToken);

        SetAuthCookies(result);
        return Ok(ServiceResponses<StaffAuthResponse>.Ok(
            new StaffAuthResponse { AccessTokenExpiresAt = result.AccessTokenExpiresAt },
            "Invitation accepted."));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Reads an existing StaffAuth session if present (cookie or header); null otherwise.</summary>
    private async Task<Guid?> TryGetAuthenticatedStaffIdAsync()
    {
        AuthenticateResult auth = await HttpContext.AuthenticateAsync("StaffAuth");
        if (auth.Succeeded && Guid.TryParse(auth.Principal?.FindFirst("user_id")?.Value, out Guid id))
        {
            return id;
        }

        return null;
    }

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
