using EduTech.Shared.Features;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Auth.PlatformAdmin;

/// <summary>
/// Platform-admin CMS for RELEASE feature flags — batched rollout + incident kill-switches. Reads
/// open to any platform admin; mutations require super_admin (enforced in the service).
/// </summary>
[ApiController]
[Route("api/v1/admin/feature-flags")]
[Authorize(Policy = "PlatformAdminOnly")]
public sealed class FeatureFlagAdminController : ControllerBase
{
    private readonly IFeatureFlagAdminService _service;

    public FeatureFlagAdminController(IFeatureFlagAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<FeatureFlag>>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FeatureFlag> flags = await _service.ListAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<FeatureFlag>>.Ok(flags, "Feature flags."));
    }

    [HttpPost]
    public async Task<ActionResult<ServiceResponses<string?>>> Create(
        [FromBody] CreateFeatureFlagRequest request, CancellationToken cancellationToken)
    {
        await _service.CreateAsync(request, ClientIp(), cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Feature flag created."));
    }

    [HttpPut("{key}")]
    public async Task<ActionResult<ServiceResponses<string?>>> SetGlobal(
        string key, [FromBody] SetFeatureFlagRequest request, CancellationToken cancellationToken)
    {
        await _service.SetGlobalAsync(key, request.Enabled, ClientIp(), cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, request.Enabled ? "Feature enabled." : "Feature disabled."));
    }

    [HttpPut("{key}/schools/{schoolId:guid}")]
    public async Task<ActionResult<ServiceResponses<string?>>> SetSchoolOverride(
        string key, Guid schoolId, [FromBody] SetFeatureFlagRequest request, CancellationToken cancellationToken)
    {
        await _service.SetSchoolOverrideAsync(key, schoolId, request.Enabled, ClientIp(), cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "School override set."));
    }

    [HttpDelete("{key}/schools/{schoolId:guid}")]
    public async Task<ActionResult<ServiceResponses<string?>>> ClearSchoolOverride(
        string key, Guid schoolId, CancellationToken cancellationToken)
    {
        await _service.ClearSchoolOverrideAsync(key, schoolId, ClientIp(), cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "School override cleared."));
    }

    private string? ClientIp()
    {
        return Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
