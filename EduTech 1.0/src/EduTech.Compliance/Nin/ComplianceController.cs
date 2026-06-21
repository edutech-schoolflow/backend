using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Compliance.Nin;

/// <summary>NIN compliance for staff + parents (the authenticated actor is resolved from the token).</summary>
[ApiController]
[Route("api/v1/compliance")]
[Authorize(Policy = "ComplianceActor")]
public sealed class ComplianceController : ControllerBase
{
    private readonly IComplianceService _service;

    public ComplianceController(IComplianceService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponses<ComplianceRecordResponse>>> Record(
        CancellationToken cancellationToken)
    {
        ComplianceRecordResponse record = await _service.GetRecordAsync(cancellationToken);
        return Ok(ServiceResponses<ComplianceRecordResponse>.Ok(record, "Compliance record."));
    }

    [HttpPost("nin")]
    public async Task<ActionResult<ServiceResponses<ComplianceRecordResponse>>> SubmitNin(
        [FromBody] SubmitNinRequest request, CancellationToken cancellationToken)
    {
        ComplianceRecordResponse record = await _service.SubmitNinAsync(request, cancellationToken);
        return Ok(ServiceResponses<ComplianceRecordResponse>.Ok(record, "NIN verified."));
    }
}
