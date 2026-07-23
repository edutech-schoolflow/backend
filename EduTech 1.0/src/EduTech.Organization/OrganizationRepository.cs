using EduTech.Organization.Domain;
using EduTech.Shared.Persistence;

namespace EduTech.Organization;

/// <summary>
/// Data access for the <c>organizations</c> root (EDD-010). Non-tenant — an organization is a
/// top-level account, keyed by id/slug. During Sprint D this is a shadow root: created and
/// backfilled, read by nothing in production yet; future sprints (onboarding-org-first, FK
/// re-pointing) consume it.
/// </summary>
internal interface IOrganizationRepository
{
    Task<Guid> CreateAsync(string name, string slug, string type, Guid? ownerMembershipId,
        CancellationToken cancellationToken);
    Task<Domain.Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Domain.Organization?> GetBySlugAsync(string slug, CancellationToken cancellationToken);
    Task RenameAsync(Guid id, string name, CancellationToken cancellationToken);
    Task SetStatusAsync(Guid id, OrganizationStatus status, CancellationToken cancellationToken);
}

internal sealed class OrganizationRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Type { get; init; } = OrganizationType.School;
    public string Status { get; init; } = "active";
    public Guid? OwnerMembershipId { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class OrganizationRepository : BaseRepository, IOrganizationRepository
{
    private const string Columns =
        "id AS Id, name AS Name, slug AS Slug, type AS Type, status, " +
        "owner_membership_id AS OwnerMembershipId, created_at AS CreatedAt";

    public OrganizationRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<Guid> CreateAsync(string name, string slug, string type, Guid? ownerMembershipId,
        CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO organizations (name, slug, type, owner_membership_id)
            VALUES (@Name, @Slug, @Type, @OwnerMembershipId)
            RETURNING id
            """,
            new { Name = name, Slug = slug, Type = type, OwnerMembershipId = ownerMembershipId },
            cancellationToken);
    }

    public async Task<Domain.Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        OrganizationRow? row = await QuerySingleOrDefaultAsync<OrganizationRow>(
            $"SELECT {Columns} FROM organizations WHERE id = @Id", new { Id = id }, cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public async Task<Domain.Organization?> GetBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        OrganizationRow? row = await QuerySingleOrDefaultAsync<OrganizationRow>(
            $"SELECT {Columns} FROM organizations WHERE slug = @Slug", new { Slug = slug }, cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public Task RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE organizations SET name = @Name, updated_at = NOW() WHERE id = @Id",
            new { Id = id, Name = name }, cancellationToken);
    }

    public Task SetStatusAsync(Guid id, OrganizationStatus status, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE organizations SET status = @Status, updated_at = NOW() WHERE id = @Id",
            new { Id = id, Status = ToDb(status) }, cancellationToken);
    }

    private static string ToDb(OrganizationStatus status) => status switch
    {
        OrganizationStatus.Suspended => "suspended",
        OrganizationStatus.Archived => "archived",
        _ => "active"
    };

    private static Domain.Organization Rehydrate(OrganizationRow r) => new(
        r.Id, r.Name, r.Slug, r.Type,
        r.Status switch
        {
            "suspended" => OrganizationStatus.Suspended,
            "archived" => OrganizationStatus.Archived,
            _ => OrganizationStatus.Active
        },
        r.OwnerMembershipId, r.CreatedAt);
}
