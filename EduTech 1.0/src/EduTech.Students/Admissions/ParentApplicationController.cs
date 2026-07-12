using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.Admissions;

/// <summary>Parent-side admissions — apply a child to a school and track it. Ownership-scoped.</summary>
[ApiController]
[Route("api/v1/family/applications")]
[Authorize(Policy = "AuthenticatedIdentity")]
public sealed class ParentApplicationController : ControllerBase
{
    private readonly IParentApplicationService _service;

    public ParentApplicationController(IParentApplicationService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> Submit(
        [FromBody] SubmitApplicationRequest request, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.SubmitAsync(request, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Application submitted."));
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ApplicationResponse>>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationResponse> apps = await _service.ListAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ApplicationResponse>>.Ok(apps, "Applications."));
    }

    [HttpGet("{applicationId:guid}")]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> Get(
        Guid applicationId, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.GetAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Application."));
    }

    /// <summary>Pay the application fee (stub: marks paid; Monnify lands with the Fees module).</summary>
    [HttpPost("{applicationId:guid}/pay")]
    public async Task<ActionResult<ServiceResponses<ApplicationResponse>>> Pay(
        Guid applicationId, CancellationToken cancellationToken)
    {
        ApplicationResponse app = await _service.PayAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<ApplicationResponse>.Ok(app, "Application fee paid."));
    }
}
