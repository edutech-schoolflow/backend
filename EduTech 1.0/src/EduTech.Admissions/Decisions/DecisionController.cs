using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Admissions.Decisions;

/// <summary>
/// An application's admissions decisions (EDD-014 Slice 6): approved / conditional / waitlisted /
/// rejected / withdrawn. Append-only. Recording a decision moves the application to 'decided'.
/// Reads gate on Student.Read, writes on Admissions.Manage.
/// </summary>
[ApiController]
[Route("api/v1/admissions/applications/{applicationId:guid}/decisions")]
[Authorize(Policy = "SchoolPortal")]
public sealed class DecisionController : ControllerBase
{
    private readonly IDecisionService _service;

    public DecisionController(IDecisionService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<DecisionResponse>>> Record(
        Guid applicationId, [FromBody] RecordDecisionRequest request, CancellationToken cancellationToken)
    {
        DecisionResponse decision = await _service.RecordAsync(applicationId, request, cancellationToken);
        return Ok(ServiceResponses<DecisionResponse>.Ok(decision, "Decision recorded."));
    }

    [HttpGet]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<DecisionResponse>>>> List(
        Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<DecisionResponse> decisions = await _service.ListAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<DecisionResponse>>.Ok(decisions, "Decisions."));
    }
}
