using EduTech.Shared.Models;
using EduTech.Shared.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Workforce;

/// <summary>A job a school can employ someone into (EDD-003 Position aggregate).</summary>
public sealed class PositionResponse
{
    public required Guid Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public required bool IsAcademic { get; init; }
}

/// <summary>
/// WORKFORCE context (EDD-002) — first resident of the module the staff/employment code migrates
/// into (V6). The platform-seeded positions list: reference data the invite/hiring UIs pick from,
/// backend-driven like /class-levels.
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

public interface IPositionRepository
{
    /// <summary>The platform-seeded (global) positions, teaching first then alphabetical.</summary>
    Task<IReadOnlyList<PositionResponse>> ListGlobalAsync(CancellationToken cancellationToken);
}

internal sealed class PositionRepository : BaseRepository, IPositionRepository
{
    public PositionRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<IReadOnlyList<PositionResponse>> ListGlobalAsync(CancellationToken cancellationToken)
    {
        return QueryAsync<PositionResponse>(
            """
            SELECT id AS Id, slug AS Slug, name AS Name, is_academic AS IsAcademic
            FROM positions
            WHERE school_id IS NULL
            ORDER BY is_academic DESC, name
            """,
            null, cancellationToken);
    }
}
