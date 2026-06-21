using EduTech.Auth.RefreshTokens;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Auth;

/// <summary>
/// Cross-actor auth endpoints. Logout is portal-agnostic — it revokes whatever refresh token the
/// caller presents (any actor) and clears the cookies.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private const string AccessCookie = "sf_access";
    private const string RefreshCookie = "sf_refresh";

    private readonly IRefreshTokenService _refreshTokenService;

    public AuthController(IRefreshTokenService refreshTokenService)
    {
        _refreshTokenService = refreshTokenService;
    }

    [HttpPost("logout")]
    public async Task<ActionResult<ServiceResponses<string?>>> Logout(CancellationToken cancellationToken)
    {
        string refreshToken = Request.Cookies[RefreshCookie] ?? string.Empty;
        await _refreshTokenService.RevokeAsync(refreshToken, cancellationToken);

        Response.Cookies.Delete(AccessCookie);
        Response.Cookies.Delete(RefreshCookie);

        return Ok(ServiceResponses<string?>.Ok(null, "Logged out."));
    }
}
