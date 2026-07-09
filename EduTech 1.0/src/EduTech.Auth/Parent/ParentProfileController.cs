using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Auth.Parent;

/// <summary>
/// Parent PROFILE creation — owned by the parent context, not by Auth's identity flows (EDD-003:
/// profiles describe a person; they never authenticate). Creating the profile does NOT change the
/// session: the client follows up with <c>POST /api/v1/auth/select-context</c> to enter it.
/// </summary>
[ApiController]
[Route("api/v1/parents")]
public sealed class ParentProfileController : ControllerBase
{
    private readonly IParentProfileService _service;

    public ParentProfileController(IParentProfileService service)
    {
        _service = service;
    }

    /// <summary>Creates (idempotently) the signed-in identity's parent profile; returns its context id.</summary>
    [HttpPost("profile")]
    [Authorize(Policy = "AuthenticatedIdentity")]
    public async Task<ActionResult<ServiceResponses<object>>> Create(CancellationToken cancellationToken)
    {
        string? userType = User.FindFirst("user_type")?.Value;
        string? sub = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
        if (userType is null || !Guid.TryParse(sub, out Guid actorId))
        {
            return Unauthorized();
        }

        Guid parentId = await _service.ProvisionAsync(userType, actorId, cancellationToken);
        return Ok(ServiceResponses<object>.Ok(new { contextId = parentId, type = "parent" },
            "Parent profile ready."));
    }
}
