using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Auth.Unified;

/// <summary>
/// The identity surface (EDD-005): retrieving the current person is IDENTITY territory, not
/// authentication — Auth keeps login/register/refresh/sessions; this owns "who am I" and the
/// platform home. Works with ANY session kind (identity-scope or portal token).
/// </summary>
[ApiController]
[Route("api/v1/identity")]
[Authorize(Policy = "AuthenticatedIdentity")]
public sealed class IdentityController : ControllerBase
{
    private readonly IUnifiedAuthService _service;

    public IdentityController(IUnifiedAuthService service)
    {
        _service = service;
    }

    /// <summary>Who am I — identity, profiles, capabilities, contexts.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<ServiceResponses<UnifiedMeResponse>>> Me(CancellationToken cancellationToken)
    {
        // Org-context tokens carry identity_id directly — the actor-resolution fallback serves
        // sessions minted before this claim existed and dies with them.
        if (Guid.TryParse(User.FindFirst("identity_id")?.Value, out Guid identityId))
        {
            UnifiedMeResponse direct = await _service.GetMeByIdentityAsync(identityId, CurrentContextId(), cancellationToken);
            return Ok(ServiceResponses<UnifiedMeResponse>.Ok(direct, "Profile."));
        }

        if (!TryGetActor(out string userType, out Guid actorId))
        {
            return Unauthorized();
        }

        UnifiedMeResponse me = await _service.GetMeAsync(userType, actorId, CurrentContextId(), cancellationToken);
        return Ok(ServiceResponses<UnifiedMeResponse>.Ok(me, "Profile."));
    }

    /// <summary>
    /// The platform home in ONE call: identity, profiles, capabilities, organizations, pending
    /// invitations, draft organizations, family counts. The landing page renders from this alone.
    /// </summary>
    [HttpGet("home")]
    public async Task<ActionResult<ServiceResponses<PlatformHomeProjection>>> Home(CancellationToken cancellationToken)
    {
        Guid identityId;
        if (!Guid.TryParse(User.FindFirst("identity_id")?.Value, out identityId))
        {
            if (!TryGetActor(out string userType, out Guid actorId))
            {
                return Unauthorized();
            }
            identityId = await _service.ResolveIdentityIdAsync(userType, actorId, cancellationToken);
        }

        PlatformHomeProjection home = await _service.GetPlatformHomeAsync(identityId, CurrentContextId(), cancellationToken);
        return Ok(ServiceResponses<PlatformHomeProjection>.Ok(home, "Home."));
    }

    private Guid? CurrentContextId() =>
        Guid.TryParse(User.FindFirst("context_id")?.Value, out Guid cc) ? cc : null;

    private bool TryGetActor(out string userType, out Guid actorId)
    {
        userType = User.FindFirst("user_type")?.Value ?? string.Empty;
        string? sub = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
        actorId = Guid.Empty;
        return userType.Length > 0 && Guid.TryParse(sub, out actorId);
    }
}
