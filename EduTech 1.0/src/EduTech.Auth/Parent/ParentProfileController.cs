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
[Route("api/v1/family")]
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

    /// <summary>The identity's family profile state (P7: works with ANY session, before the profile exists).</summary>
    [HttpGet("profile")]
    [Authorize(Policy = "AuthenticatedIdentity")]
    public async Task<ActionResult<ServiceResponses<FamilyProfileResponse>>> Get(CancellationToken cancellationToken)
    {
        string? userType = User.FindFirst("user_type")?.Value;
        string? sub = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
        if (userType is null || !Guid.TryParse(sub, out Guid actorId))
        {
            return Unauthorized();
        }

        FamilyProfileResponse profile = await _service.GetFamilyProfileAsync(userType, actorId, cancellationToken);
        return Ok(ServiceResponses<FamilyProfileResponse>.Ok(profile, "Family profile."));
    }

    /// <summary>Sets the payment PIN on the identity's family profile.</summary>
    [HttpPost("payment-pin")]
    [Authorize(Policy = "AuthenticatedIdentity")]
    public async Task<ActionResult<ServiceResponses<string?>>> SetPaymentPin(
        [FromBody] SetPaymentPinRequest request, CancellationToken cancellationToken)
    {
        string? userType = User.FindFirst("user_type")?.Value;
        string? sub = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
        if (userType is null || !Guid.TryParse(sub, out Guid actorId))
        {
            return Unauthorized();
        }

        await _service.SetPaymentPinAsync(userType, actorId, request.Pin, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Payment PIN set."));
    }
}
