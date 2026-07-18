using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Admissions.Inquiries;

/// <summary>
/// Admission inquiries (EDD-014 Slice 2) — the school-side pipeline of prospective families: log an
/// inquiry, mark contacted, book a visit, close. Reads gate on Student.Read, writes on Admissions.Manage.
/// (Public/parent self-service inquiry submission is a later, unauthenticated surface.)
/// </summary>
[ApiController]
[Route("api/v1/admissions/inquiries")]
[Authorize(Policy = "SchoolPortal")]
public sealed class InquiryController : ControllerBase
{
    private readonly IInquiryService _service;

    public InquiryController(IInquiryService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<InquiryResponse>>> Create(
        [FromBody] CreateInquiryRequest request, CancellationToken cancellationToken)
    {
        InquiryResponse inquiry = await _service.CreateAsync(request, cancellationToken);
        return Ok(ServiceResponses<InquiryResponse>.Ok(inquiry, "Inquiry logged."));
    }

    [HttpGet]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<InquiryResponse>>>> List(
        [FromQuery] string? status, CancellationToken cancellationToken)
    {
        IReadOnlyList<InquiryResponse> inquiries = await _service.ListAsync(status, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<InquiryResponse>>.Ok(inquiries, "Inquiries."));
    }

    [HttpGet("{inquiryId:guid}")]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<InquiryResponse>>> Get(
        Guid inquiryId, CancellationToken cancellationToken)
    {
        InquiryResponse inquiry = await _service.GetAsync(inquiryId, cancellationToken);
        return Ok(ServiceResponses<InquiryResponse>.Ok(inquiry, "Inquiry."));
    }

    [HttpPost("{inquiryId:guid}/contacted")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<InquiryResponse>>> MarkContacted(
        Guid inquiryId, CancellationToken cancellationToken)
    {
        InquiryResponse inquiry = await _service.MarkContactedAsync(inquiryId, cancellationToken);
        return Ok(ServiceResponses<InquiryResponse>.Ok(inquiry, "Marked contacted."));
    }

    [HttpPost("{inquiryId:guid}/visit")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<InquiryResponse>>> BookVisit(
        Guid inquiryId, [FromBody] BookVisitRequest request, CancellationToken cancellationToken)
    {
        InquiryResponse inquiry = await _service.BookVisitAsync(inquiryId, request.VisitAt, cancellationToken);
        return Ok(ServiceResponses<InquiryResponse>.Ok(inquiry, "Visit booked."));
    }

    [HttpPost("{inquiryId:guid}/close")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<InquiryResponse>>> Close(
        Guid inquiryId, CancellationToken cancellationToken)
    {
        InquiryResponse inquiry = await _service.CloseAsync(inquiryId, cancellationToken);
        return Ok(ServiceResponses<InquiryResponse>.Ok(inquiry, "Inquiry closed."));
    }
}
