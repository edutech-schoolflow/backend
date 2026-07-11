using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.Admissions;

/// <summary>
/// Identity-space "my applications" (EDD-002): the signed-in identity's admissions across schools,
/// authorized by the identity session. Applying is a pre-membership act, so it belongs to the
/// identity, not a workspace. Empty until the person has applied.
/// </summary>
[ApiController]
[Route("api/v1/identity/applications")]
[Authorize(Policy = "AuthenticatedIdentity")]
public sealed class IdentityApplicationsController : ControllerBase
{
    private readonly IParentApplicationService _service;

    public IdentityApplicationsController(IParentApplicationService service)
    {
        _service = service;
    }

    /// <summary>My applications, most recent first. Empty until I've applied anywhere.</summary>
    [HttpGet]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ApplicationResponse>>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationResponse> applications = await _service.ListMineAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ApplicationResponse>>.Ok(applications, "Applications."));
    }
}
