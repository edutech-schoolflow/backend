using EduTech.Shared.Auth;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.Academics.Transition;

/// <summary>
/// Term/session transition (SchoolPortal). Anyone in the portal can SEE where the school stands
/// (the dashboard banner); CONFIRMING the move — which flips what attendance, grades and fees hang
/// off — is a leadership action (the owner bypasses role checks).
/// </summary>
[ApiController]
[Authorize(Policy = "SchoolPortal")]
public sealed class AcademicTransitionController : ControllerBase
{
    private readonly IAcademicTransitionService _service;

    public AcademicTransitionController(IAcademicTransitionService service)
    {
        _service = service;
    }

    /// <summary>The transition state: nothing due, or the prepared next term/session awaiting confirm.</summary>
    [HttpGet("api/v1/academics/transition")]
    public async Task<ActionResult<ServiceResponses<TransitionProposalResponse>>> Get(CancellationToken cancellationToken)
    {
        TransitionProposalResponse proposal = await _service.GetProposalAsync(null, cancellationToken);
        return Ok(ServiceResponses<TransitionProposalResponse>.Ok(proposal, "Transition state."));
    }

    /// <summary>Apply the due transition (session moves are gated on promotion being complete).</summary>
    [HttpPost("api/v1/academics/transition/confirm")]
    [RequireRole(StaffRoles.Principal, StaffRoles.VicePrincipal, StaffRoles.SchoolAdmin)]
    public async Task<ActionResult<ServiceResponses<TransitionProposalResponse>>> Confirm(CancellationToken cancellationToken)
    {
        TransitionProposalResponse state = await _service.ConfirmAsync(null, cancellationToken);
        return Ok(ServiceResponses<TransitionProposalResponse>.Ok(state, "Transition applied."));
    }
}
