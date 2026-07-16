using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
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

/// <summary>
/// The full PUBLIC profile of a school — everything a parent may see before applying: identity,
/// contact details, and the class ladder. Nothing internal (finances, rosters) ever rides here.
/// </summary>
public sealed class ParentSchoolProfileResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Type { get; init; }
    public string? Location { get; init; }
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? LogoUrl { get; init; }
    public bool Verified { get; init; }
    public decimal ApplicationFee { get; init; }
    public required IReadOnlyList<ParentSchoolClass> Classes { get; init; }
}

/// <summary>A class a school offers, as a parent picking a desired class sees it.</summary>
public sealed class ParentSchoolClass
{
    public required string Name { get; init; }
    public required string Stage { get; init; }   // snake_case level (nursery | primary | …)
    public required int Order { get; init; }
}

public interface IParentSchoolDirectoryService
{
    Task<IReadOnlyList<ParentSchoolListItem>> SearchAsync(string? query, string? type, CancellationToken cancellationToken);
    Task<ParentSchoolProfileResponse> GetAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ParentSchoolClass>> GetClassesAsync(Guid schoolId, CancellationToken cancellationToken);
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

    public async Task<ParentSchoolProfileResponse> GetAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        ParentSchoolProfileRow row = await _repository.GetByIdAsync(schoolId, cancellationToken)
            ?? throw new AppErrorException("School not found.", 404, ErrorCodes.NotFound);
        IReadOnlyList<ParentSchoolClass> classes = await _repository.GetClassesAsync(schoolId, cancellationToken);

        return new ParentSchoolProfileResponse
        {
            Id = row.Id,
            Name = row.Name,
            Type = row.Type,
            Location = row.Location,
            Address = row.Address,
            Phone = row.Phone,
            Email = row.Email,
            LogoUrl = row.LogoUrl,
            Verified = row.Verified,
            ApplicationFee = row.ApplicationFee,
            Classes = classes
        };
    }

    public Task<IReadOnlyList<ParentSchoolClass>> GetClassesAsync(Guid schoolId, CancellationToken cancellationToken)
        => _repository.GetClassesAsync(schoolId, cancellationToken);
}

internal sealed class ParentSchoolProfileRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? Location { get; init; }
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? LogoUrl { get; init; }
    public bool Verified { get; init; }
    public decimal ApplicationFee { get; init; }
}

internal interface IParentSchoolDirectoryRepository
{
    Task<IReadOnlyList<ParentSchoolListItem>> SearchAsync(string? query, string? type, CancellationToken cancellationToken);
    Task<ParentSchoolProfileRow?> GetByIdAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ParentSchoolClass>> GetClassesAsync(Guid schoolId, CancellationToken cancellationToken);
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

    public Task<ParentSchoolProfileRow?> GetByIdAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        // Only publicly-listed schools are viewable — a parent can't open a hidden school's profile.
        return QuerySingleOrDefaultAsync<ParentSchoolProfileRow?>(
            """
            SELECT s.id AS Id,
                   s.name AS Name,
                   s.type AS Type,
                   NULLIF(concat_ws(', ', s.city, s.state), '') AS Location,
                   s.address AS Address,
                   s.phone AS Phone,
                   s.email AS Email,
                   s.logo_url AS LogoUrl,
                   (s.kyc_status = 'approved') AS Verified,
                   0::numeric AS ApplicationFee
            FROM schools s
            WHERE s.id = @Id AND s.visibility = 'public' AND s.name IS NOT NULL AND s.name <> ''
            """,
            new { Id = schoolId }, cancellationToken);
    }

    public Task<IReadOnlyList<ParentSchoolClass>> GetClassesAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        // The classes a public school offers, in ladder order — the parent's desired-class options.
        return QueryAsync<ParentSchoolClass>(
            """
            SELECT c.name AS Name, c.level AS Stage, c.display_order AS Order
            FROM classes c
            JOIN schools s ON s.id = c.school_id
            WHERE c.school_id = @Id AND s.visibility = 'public'
            ORDER BY c.display_order, c.name
            """,
            new { Id = schoolId }, cancellationToken);
    }
}
