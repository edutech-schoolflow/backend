using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Auth.Unified;

/// <summary>
/// Organization onboarding (EDD-004 Workflow 1): a signed-in, verified identity creates a school.
/// This is NOT registration — the account exists first; this creates the organization + the owner
/// employment and hands back the owner context (cookies), landing them in their new school.
/// </summary>
[ApiController]
[Route("api/v1/organizations")]
public sealed class OrganizationOnboardingController : ControllerBase
{
    private const string AccessCookie = "sf_access";
    private const string RefreshCookie = "sf_refresh";

    private readonly IUnifiedAuthService _service;

    public OrganizationOnboardingController(IUnifiedAuthService service)
    {
        _service = service;
    }

    /// <summary>Resolves a workspace URL (/o/{slug}) — the organization + the caller's context there.</summary>
    [HttpGet("{slug}")]
    [Authorize(Policy = "AuthenticatedIdentity")]
    public async Task<ActionResult<ServiceResponses<OrganizationWorkspaceResponse>>> GetBySlug(
        string slug, CancellationToken cancellationToken)
    {
        Guid? identityId = await ResolveIdentityIdAsync(cancellationToken);
        if (identityId is null)
        {
            return Unauthorized();
        }

        OrganizationWorkspaceResponse workspace =
            await _service.GetOrganizationWorkspaceAsync(identityId.Value, slug, cancellationToken);
        return Ok(ServiceResponses<OrganizationWorkspaceResponse>.Ok(workspace, "Workspace."));
    }

    /// <summary>Organization Wizard: names a bootstrapped org (owner-only). Returns the workspace at its
    /// new slug — the caller re-routes to /o/{newSlug}.</summary>
    [HttpPatch("{slug}")]
    [Authorize(Policy = "AuthenticatedIdentity")]
    public async Task<ActionResult<ServiceResponses<OrganizationWorkspaceResponse>>> Setup(
        string slug, [FromBody] SetupOrganizationRequest request, CancellationToken cancellationToken)
    {
        Guid? identityId = await ResolveIdentityIdAsync(cancellationToken);
        if (identityId is null)
        {
            return Unauthorized();
        }

        OrganizationWorkspaceResponse workspace =
            await _service.SetupOrganizationAsync(identityId.Value, slug, request, cancellationToken);
        return Ok(ServiceResponses<OrganizationWorkspaceResponse>.Ok(workspace, "School set up."));
    }

    /// <summary>Form-first create: the school is named up front, so an abandoned form writes nothing.</summary>
    [HttpPost]
    [Authorize(Policy = "AuthenticatedIdentity")]
    public async Task<ActionResult<ServiceResponses<UnifiedLoginResponse>>> Create(
        [FromBody] SetupOrganizationRequest request, CancellationToken cancellationToken)
    {
        string? userType = User.FindFirst("user_type")?.Value;
        string? sub = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
        if (userType is null || !Guid.TryParse(sub, out Guid actorId))
        {
            return Unauthorized();
        }

        UnifiedLoginResult result = await _service.CreateOrganizationAsync(userType, actorId, request,
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
                ?? HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.FirstOrDefault(), cancellationToken);

        if (result.Tokens is UnifiedTokens tokens)
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

        return Ok(ServiceResponses<UnifiedLoginResponse>.Ok(
            new UnifiedLoginResponse { Contexts = result.Contexts, Selected = result.Selected },
            "Your school is ready. Welcome to your workspace."));
    }

    /// <summary>Org-context tokens carry identity_id; older sessions resolve via their portal actor.</summary>
    private async Task<Guid?> ResolveIdentityIdAsync(CancellationToken cancellationToken)
    {
        if (Guid.TryParse(User.FindFirst("identity_id")?.Value, out Guid identityId))
        {
            return identityId;
        }

        string? userType = User.FindFirst("user_type")?.Value;
        string? sub = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
        if (userType is null || !Guid.TryParse(sub, out Guid actorId))
        {
            return null;
        }

        return await _service.ResolveIdentityIdAsync(userType, actorId, cancellationToken);
    }
}
