using EduTech.Shared.Persistence;

namespace EduTech.Students.ParentFacing;

/// <summary>
/// A school as seen by a parent browsing the directory to apply. Public, not ownership-scoped —
/// any authenticated parent can discover schools. (Application fee has no per-school config yet, so
/// it is reported as 0; submitting an application does not gate on KYC/visibility today.)
/// </summary>
public sealed class ParentSchoolListItem
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Type { get; init; }
    public string? Location { get; init; }
    public bool Verified { get; init; }
    public decimal ApplicationFee { get; init; }
}

public interface IParentSchoolDirectoryService
{
    Task<IReadOnlyList<ParentSchoolListItem>> SearchAsync(string? query, string? type, CancellationToken cancellationToken);
}

internal sealed class ParentSchoolDirectoryService : IParentSchoolDirectoryService
{
    private readonly IParentSchoolDirectoryRepository _repository;

    public ParentSchoolDirectoryService(IParentSchoolDirectoryRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<ParentSchoolListItem>> SearchAsync(string? query, string? type,
        CancellationToken cancellationToken)
        => _repository.SearchAsync(query, type, cancellationToken);
}

internal interface IParentSchoolDirectoryRepository
{
    Task<IReadOnlyList<ParentSchoolListItem>> SearchAsync(string? query, string? type, CancellationToken cancellationToken);
}

internal sealed class ParentSchoolDirectoryRepository : BaseRepository, IParentSchoolDirectoryRepository
{
    public ParentSchoolDirectoryRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<IReadOnlyList<ParentSchoolListItem>> SearchAsync(string? query, string? type,
        CancellationToken cancellationToken)
    {
        // Parents only discover schools that have opted into public listing (visibility = 'public').
        // Such a school has completed setup + KYC, so it can actually receive and process applications.
        string? like = string.IsNullOrWhiteSpace(query) ? null : $"%{query.Trim()}%";
        string? typeFilter = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();

        return QueryAsync<ParentSchoolListItem>(
            """
            SELECT s.id AS Id,
                   s.name AS Name,
                   s.type AS Type,
                   NULLIF(concat_ws(', ', s.city, s.state), '') AS Location,
                   (s.kyc_status = 'approved') AS Verified,
                   0::numeric AS ApplicationFee
            FROM schools s
            WHERE s.visibility = 'public' AND s.name IS NOT NULL AND s.name <> ''
              AND (@Like IS NULL OR s.name ILIKE @Like OR s.city ILIKE @Like OR s.state ILIKE @Like)
              AND (@Type IS NULL OR lower(s.type) = @Type)
            ORDER BY s.name
            """,
            new { Like = like, Type = typeFilter }, cancellationToken);
    }
}
