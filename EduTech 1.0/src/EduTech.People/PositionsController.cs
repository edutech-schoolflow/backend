using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.People;

/// <summary>
/// The platform-seeded positions list: reference data the invite/hiring UIs pick from,
/// backend-driven like /class-levels. Relocated into the People foundation context (EDD-008);
/// route and response shape are unchanged.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class PositionsController : ControllerBase
{
    private readonly IPositionRepository _positions;

    public PositionsController(IPositionRepository positions)
    {
        _positions = positions;
    }

    [HttpGet("api/v1/positions")]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<PositionResponse>>>> Get(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PositionResponse> positions = await _positions.ListGlobalAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<PositionResponse>>.Ok(positions, "Positions."));
    }
}
