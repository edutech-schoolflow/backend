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
        return await ExecuteScalarAsync<Guid>(
            "INSERT INTO schools DEFAULT VALUES RETURNING id",
            parameters: null,
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
