using EduTech.Shared.Audit;
using EduTech.Shared.Auth;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Api.Controllers;

/// <summary>The school's audit trail — read-only. Every auditable domain event lands here via its observer.</summary>
[ApiController]
[Route("api/v1/school/audit-log")]
[Authorize(Policy = "SchoolPortal")]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditLogRepository _repository;

    public AuditController(IAuditLogRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [RequireFeature(StaffFeatureFlags.ViewSchoolOverview)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<AuditLogEntry>>>> List(
        [FromQuery] string? entityType, [FromQuery] Guid? entityId,
        [FromQuery] int page = 1, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        int safePage = page < 1 ? 1 : page;
        int safeLimit = limit is < 1 or > 100 ? 50 : limit;

        IReadOnlyList<AuditLogEntry> entries = await _repository.ListAsync(
            entityType, entityId, (safePage - 1) * safeLimit, safeLimit, cancellationToken);

        return Ok(ServiceResponses<IReadOnlyList<AuditLogEntry>>.Ok(entries, "Audit log."));
    }
}
