using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Admissions.Offers;

/// <summary>
/// An application's offers (EDD-014 Slice 7): issue → accept / decline / withdraw. First-class offer
/// (campus, class, fee plan, deadline). One outstanding offer per application; issued from an
/// approved/conditional decision. Reads gate on Student.Read, writes on Admissions.Manage.
/// (Family self-service accept/decline is a later parent-facing surface.)
/// </summary>
[ApiController]
[Route("api/v1/admissions/applications/{applicationId:guid}/offers")]
[Authorize(Policy = "SchoolPortal")]
public sealed class OfferController : ControllerBase
{
    private readonly IOfferService _service;

    public OfferController(IOfferService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<OfferResponse>>> Issue(
        Guid applicationId, [FromBody] IssueOfferRequest request, CancellationToken cancellationToken)
    {
        OfferResponse offer = await _service.IssueAsync(applicationId, request, cancellationToken);
        return Ok(ServiceResponses<OfferResponse>.Ok(offer, "Offer issued."));
    }

    [HttpGet]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<OfferResponse>>>> List(
        Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<OfferResponse> offers = await _service.ListAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<OfferResponse>>.Ok(offers, "Offers."));
    }

    [HttpPost("{offerId:guid}/accept")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<OfferResponse>>> Accept(
        Guid applicationId, Guid offerId, CancellationToken cancellationToken)
    {
        OfferResponse offer = await _service.AcceptAsync(applicationId, offerId, cancellationToken);
        return Ok(ServiceResponses<OfferResponse>.Ok(offer, "Offer accepted."));
    }

    [HttpPost("{offerId:guid}/decline")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<OfferResponse>>> Decline(
        Guid applicationId, Guid offerId, CancellationToken cancellationToken)
    {
        OfferResponse offer = await _service.DeclineAsync(applicationId, offerId, cancellationToken);
        return Ok(ServiceResponses<OfferResponse>.Ok(offer, "Offer declined."));
    }

    [HttpPost("{offerId:guid}/withdraw")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<OfferResponse>>> Withdraw(
        Guid applicationId, Guid offerId, CancellationToken cancellationToken)
    {
        OfferResponse offer = await _service.WithdrawAsync(applicationId, offerId, cancellationToken);
        return Ok(ServiceResponses<OfferResponse>.Ok(offer, "Offer withdrawn."));
    }
}
