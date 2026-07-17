using EduTech.People.Domain;
using EduTech.Shared.Persistence;

namespace EduTech.People;

/// <summary>A job an organization can employ someone into (EDD-008 Position aggregate).</summary>
public sealed class PositionResponse
{
    public required Guid Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public required bool IsAcademic { get; init; }
}

/// <summary>
/// Reads the canonical <c>positions</c> catalog (EDD-008). Positions are reference data — platform
/// global defaults (<c>school_id IS NULL</c>) plus an organization's own — so this derives from
/// <see cref="BaseRepository"/>. The table column is still <c>school_id</c>; the FK re-points to
/// <c>organizations</c> in a later strangler sprint (after the EDD-010 root is stable).
/// </summary>
public interface IPositionRepository
{
    /// <summary>The platform-seeded (global) positions, teaching first then alphabetical.</summary>
    Task<IReadOnlyList<PositionResponse>> ListGlobalAsync(CancellationToken cancellationToken);

    /// <summary>Positions available to an organization: the global defaults plus its own, teaching first.</summary>
    Task<IReadOnlyList<PositionResponse>> ListForOrganizationAsync(Guid organizationId,
        CancellationToken cancellationToken);
}

internal sealed class PositionRow
{
    public Guid Id { get; init; }
    public Guid? OrganizationId { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsAcademic { get; init; }
}

internal sealed class PositionRepository : BaseRepository, IPositionRepository
{
    public PositionRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<IReadOnlyList<PositionResponse>> ListGlobalAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PositionRow> rows = await QueryAsync<PositionRow>(
            """
            SELECT id AS Id, school_id AS OrganizationId, slug AS Slug, name AS Name,
                   is_academic AS IsAcademic
            FROM positions
            WHERE school_id IS NULL
            ORDER BY is_academic DESC, name
            """,
            null, cancellationToken);

        return rows.Select(Rehydrate).Select(ToResponse).ToList();
    }

    public async Task<IReadOnlyList<PositionResponse>> ListForOrganizationAsync(Guid organizationId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PositionRow> rows = await QueryAsync<PositionRow>(
            """
            SELECT id AS Id, school_id AS OrganizationId, slug AS Slug, name AS Name,
                   is_academic AS IsAcademic
            FROM positions
            WHERE school_id IS NULL OR school_id = @OrganizationId
            ORDER BY is_academic DESC, name
            """,
            new { OrganizationId = organizationId }, cancellationToken);

        return rows.Select(Rehydrate).Select(ToResponse).ToList();
    }

    // The catalog rehydrates through the aggregate so its invariants hold on every read.
    private static Position Rehydrate(PositionRow r) =>
        new(r.Id, r.OrganizationId, r.Slug, r.Name, r.IsAcademic);

    private static PositionResponse ToResponse(Position p) => new()
    {
        Id = p.Id,
        Slug = p.Slug,
        Name = p.Name,
        IsAcademic = p.IsAcademic
    };
}
