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
    private readonly ISchoolStaffService _staffService;

    public SchoolStaffController(IStaffInviteService inviteService, ISchoolStaffService staffService)
    {
        _inviteService = inviteService;
        _staffService = staffService;
    }

    /// <summary>The school's staff directory (every affiliation + identity, any status).</summary>
    [HttpGet]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<StaffDirectoryItemResponse>>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StaffDirectoryItemResponse> staff = await _staffService.ListAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<StaffDirectoryItemResponse>>.Ok(staff, "Staff directory."));
    }

    [HttpPost("invite")]
    public async Task<ActionResult<ServiceResponses<InviteStaffResponse>>> Invite(
        [FromBody] InviteStaffRequest request, CancellationToken cancellationToken)
    {
        InviteStaffResponse result = await _inviteService.InviteAsync(request, cancellationToken);
        return Ok(ServiceResponses<InviteStaffResponse>.Ok(result, "Invitation sent."));
    }

    [HttpPatch("{affiliationId:guid}")]
    public async Task<ActionResult<ServiceResponses<StaffDirectoryItemResponse>>> UpdateRole(
        Guid affiliationId, [FromBody] UpdateStaffRoleRequest request, CancellationToken cancellationToken)
    {
        StaffDirectoryItemResponse staff = await _staffService.UpdateRoleAsync(affiliationId, request, cancellationToken);
        return Ok(ServiceResponses<StaffDirectoryItemResponse>.Ok(staff, "Staff updated."));
    }

    [HttpPost("{affiliationId:guid}/deactivate")]
    public async Task<ActionResult<ServiceResponses<StaffDirectoryItemResponse>>> Deactivate(
        Guid affiliationId, CancellationToken cancellationToken)
    {
        StaffDirectoryItemResponse staff = await _staffService.DeactivateAsync(affiliationId, cancellationToken);
        return Ok(ServiceResponses<StaffDirectoryItemResponse>.Ok(staff, "Staff deactivated."));
    }

    [HttpPost("{affiliationId:guid}/reactivate")]
    public async Task<ActionResult<ServiceResponses<StaffDirectoryItemResponse>>> Reactivate(
        Guid affiliationId, CancellationToken cancellationToken)
    {
        StaffDirectoryItemResponse staff = await _staffService.ReactivateAsync(affiliationId, cancellationToken);
        return Ok(ServiceResponses<StaffDirectoryItemResponse>.Ok(staff, "Staff reactivated."));
    }

    [HttpPost("{affiliationId:guid}/resend-invite")]
    public async Task<ActionResult<ServiceResponses<InviteStaffResponse>>> ResendInvite(
        Guid affiliationId, CancellationToken cancellationToken)
    {
        InviteStaffResponse result = await _inviteService.ResendInviteAsync(affiliationId, cancellationToken);
        return Ok(ServiceResponses<InviteStaffResponse>.Ok(result, "Invitation resent."));
    }
}
