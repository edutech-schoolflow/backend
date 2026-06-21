using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.School.Kyc;

/// <summary>School-owner KYC submission + status (Actor 1). Multipart upload of details + documents.</summary>
[ApiController]
[Route("api/v1/school/kyc")]
[Authorize(Policy = "SchoolOnly")]
public sealed class SchoolKycController : ControllerBase
{
    private readonly ISchoolKycService _service;

    public SchoolKycController(ISchoolKycService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequestSizeLimit(60 * 1024 * 1024)] // 5 documents × ~10 MB + headroom
    public async Task<ActionResult<ServiceResponses<KycSubmissionResponse>>> Submit(
        [FromForm] SubmitKycRequest request, CancellationToken cancellationToken)
    {
        KycSubmissionResponse result = await _service.SubmitAsync(request, cancellationToken);
        return Ok(ServiceResponses<KycSubmissionResponse>.Ok(result, "KYC submitted. We'll review it shortly."));
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponses<KycSubmissionResponse>>> Status(
        CancellationToken cancellationToken)
    {
        KycSubmissionResponse result = await _service.GetStatusAsync(cancellationToken);
        return Ok(ServiceResponses<KycSubmissionResponse>.Ok(result, "KYC status."));
    }
}
