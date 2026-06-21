using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Auth.Staff;

/// <summary>
/// School-side staff management (Actor 2, Path B). Runs under SchoolAuth — the school_id and inviter
/// come from the token via IEduTechRequestContext.
/// </summary>
[ApiController]
[Route("api/v1/school/staff")]
[Authorize(Policy = "SchoolOnly")]
public sealed class SchoolStaffController : ControllerBase
{
    private readonly IStaffInviteService _inviteService;

    public SchoolStaffController(IStaffInviteService inviteService)
    {
        _inviteService = inviteService;
    }

    [HttpPost("invite")]
    public async Task<ActionResult<ServiceResponses<InviteStaffResponse>>> Invite(
        [FromBody] InviteStaffRequest request, CancellationToken cancellationToken)
    {
        InviteStaffResponse result = await _inviteService.InviteAsync(request, cancellationToken);
        return Ok(ServiceResponses<InviteStaffResponse>.Ok(result, "Invitation sent."));
    }
}
