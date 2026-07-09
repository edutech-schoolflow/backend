using System.Data;
using EduTech.Shared.Persistence;

namespace EduTech.Auth.SchoolOwner;

/// <summary>
/// Auth's view of the <c>schools</c> table — just enough to create the school shell at registration.
/// (The School module owns the richer school/KYC operations later.)
/// </summary>
internal interface ISchoolRepository
{
    /// <summary>
    /// Creates an empty school in <c>pending_kyc</c> state (all columns default; name/subdomain
    /// are captured later during KYC) and returns its id. Runs inside the registration transaction.
    /// </summary>
    Task<Guid> CreateShellAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Status fields needed for the owner's JWT claims at login. Null if not found.</summary>
    Task<SchoolStatusRow?> GetStatusAsync(Guid schoolId, CancellationToken cancellationToken);
}

/// <summary>School status fields embedded in the owner access token.</summary>
internal sealed class SchoolStatusRow
{
    public string Status { get; init; } = string.Empty;
    public string KycStatus { get; init; } = string.Empty;
    public string? Subdomain { get; init; }
}

internal sealed class SchoolRepository : BaseRepository, ISchoolRepository
{
    public SchoolRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<Guid> CreateShellAsync(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        // Id generated here so the placeholder slug (unique NOT NULL soon) exists from birth;
        // the Organization Wizard re-slugs from the school's name later.
        Guid id = Guid.NewGuid();
        return await ExecuteScalarAsync<Guid>(
            "INSERT INTO schools (id, slug) VALUES (@Id, @Slug) RETURNING id",
            new { Id = id, Slug = $"s-{id:N}"[..10] },
            cancellationToken: cancellationToken,
            transaction: transaction);
    }

    public Task<SchoolStatusRow?> GetStatusAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<SchoolStatusRow>(
            "SELECT status, kyc_status, subdomain FROM schools WHERE id = @Id",
            new { Id = schoolId }, cancellationToken);
    }
}
