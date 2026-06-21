using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Persistence;

namespace EduTech.Compliance.Nin;

/// <summary>
/// Reads/writes the per-actor NIN + kyc_status. The NIN lives on each actor's own table
/// (staff_users / parents) — both GLOBAL tables, so this derives from <see cref="BaseRepository"/>.
/// </summary>
internal interface IComplianceRepository
{
    Task SetNinAsync(string actorType, Guid actorId, string encryptedNin, string status,
        CancellationToken cancellationToken);

    Task<ComplianceStateRow?> GetAsync(string actorType, Guid actorId, CancellationToken cancellationToken);
}

internal sealed class ComplianceStateRow
{
    public string KycStatus { get; init; } = string.Empty;
    public bool HasNin { get; init; }
}

internal sealed class ComplianceRepository : BaseRepository, IComplianceRepository
{
    public ComplianceRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task SetNinAsync(string actorType, Guid actorId, string encryptedNin, string status,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            $"UPDATE {Table(actorType)} SET nin = @Nin, kyc_status = @Status, updated_at = NOW() WHERE id = @Id",
            new { Id = actorId, Nin = encryptedNin, Status = status }, cancellationToken);
    }

    public Task<ComplianceStateRow?> GetAsync(string actorType, Guid actorId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ComplianceStateRow>(
            $"SELECT kyc_status AS KycStatus, (nin IS NOT NULL) AS HasNin FROM {Table(actorType)} WHERE id = @Id",
            new { Id = actorId }, cancellationToken);
    }

    // actorType comes from the validated JWT user_type — never raw user input — and maps to a fixed
    // table name (no SQL injection surface).
    private static string Table(string actorType) => actorType switch
    {
        UserTypes.Staff => "staff_users",
        UserTypes.Parent => "parents",
        _ => throw new AppErrorException("Compliance isn't available for this account.", 403, ErrorCodes.Forbidden)
    };
}
