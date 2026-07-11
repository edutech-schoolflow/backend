using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.ParentFacing;

/// <summary>
/// Identity-space "my children" (EDD-002): the signed-in identity's children across every school,
/// authorized by the identity session — not a parent token. This is a "my" API, not a "parent" API;
/// the /identity/{...} surface is where the identity's cross-organization data lives. (The children
/// query stays in the Students module; the dedicated Identity module owns this surface in Phase 3.)
/// </summary>
[ApiController]
[Route("api/v1/identity/children")]
[Authorize(Policy = "AuthenticatedIdentity")]
public sealed class IdentityChildrenController : ControllerBase
{
    private readonly IParentChildrenService _service;

    public IdentityChildrenController(IParentChildrenService service)
    {
        _service = service;
    }

    /// <summary>My children + their active enrollment, across all schools. Empty until a child is linked.</summary>
    [HttpGet]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ParentChildResponse>>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ParentChildResponse> children = await _service.GetMyChildrenAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ParentChildResponse>>.Ok(children, "Children."));
    }

    /// <summary>
    /// Save a child to my account (or update one I own). Provisions my parent profile on the first
    /// child. No school required — the child sits on my account, ready to apply/enrol later.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ServiceResponses<object>>> Save(
        [FromBody] UpsertChildProfileRequest request, CancellationToken cancellationToken)
    {
        Guid childProfileId = await _service.UpsertMyChildAsync(request, cancellationToken);
        return Ok(ServiceResponses<object>.Ok(new { childProfileId }, "Child saved."));
    }
}
