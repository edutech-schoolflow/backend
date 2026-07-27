using EduTech.Shared.Persistence;

namespace EduTech.Compliance.Nin;

/// <summary>
/// EDD-017 Slice 2 — Compliance owns the verification WORKFLOW; Identity owns the state. All writes are
/// keyed on the canonical <c>identity_id</c>: the verified fact lands on <c>identities</c>, every attempt on
/// the append-only <c>identity_verifications</c>. Global (identity-level), so <see cref="BaseRepository"/>.
///
/// Slice 2 keeps Auth frozen: on a successful verification it also syncs the legacy <c>kyc_status</c> for the
/// CURRENT portal (staff_users / parents), exactly as before — the NIN itself goes only to Identity. Those
/// legacy reads move to <c>identities.kyc_status</c> in Slice 3, and the sync + columns retire in Slice 5.
/// </summary>
internal interface IComplianceRepository
{
    /// <summary>The identity's composed name, to match against the NIN's registry name. Null if missing.</summary>
    Task<string?> GetIdentityNameAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>The identity's canonical KYC status (defaults to 'unverified').</summary>
    Task<string?> GetKycStatusAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>Record a succeeded attempt: append history, set Identity verified + encrypted NIN, and sync the
    /// current portal's legacy kyc_status (Slice 2 compat). Atomic.</summary>
    Task RecordSuccessAsync(Guid identityId, string provider, string encryptedNin,
        bool syncStaff, bool syncParent, CancellationToken cancellationToken);

    /// <summary>Record a failed attempt: append history, set Identity rejected. Legacy untouched (matches the
    /// prior no-persist-on-failure behaviour). Atomic.</summary>
    Task RecordFailureAsync(Guid identityId, string provider, string? reason, CancellationToken cancellationToken);
}

internal sealed class ComplianceRepository : BaseRepository, IComplianceRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ComplianceRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<string?> GetIdentityNameAsync(Guid identityId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string>(
            "SELECT concat_ws(' ', first_name, middle_name, last_name) FROM identities WHERE id = @Id",
            new { Id = identityId }, cancellationToken);
    }

    public Task<string?> GetKycStatusAsync(Guid identityId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string>(
            "SELECT kyc_status FROM identities WHERE id = @Id",
            new { Id = identityId }, cancellationToken);
    }

    public async Task RecordSuccessAsync(Guid identityId, string provider, string encryptedNin,
        bool syncStaff, bool syncParent, CancellationToken cancellationToken)
    {
        await using DbTransactionScope tx = await _connectionFactory.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            "INSERT INTO identity_verifications (identity_id, provider, outcome) VALUES (@Id, @Provider, 'succeeded')",
            new { Id = identityId, Provider = provider }, cancellationToken, tx.Transaction);

        await ExecuteAsync(
            "UPDATE identities SET kyc_status = 'verified', nin = @Nin, nin_verified_at = NOW(), updated_at = NOW() WHERE id = @Id",
            new { Id = identityId, Nin = encryptedNin }, cancellationToken, tx.Transaction);

        // Slice 2 compat: keep the current portal's legacy kyc_status in sync so Auth (still reading it) is
        // unchanged. NIN is NOT written to the legacy tables — it lives only on Identity now.
        if (syncStaff)
        {
            await ExecuteAsync("UPDATE staff_users SET kyc_status = 'verified', updated_at = NOW() WHERE identity_id = @Id",
                new { Id = identityId }, cancellationToken, tx.Transaction);
        }
        if (syncParent)
        {
            await ExecuteAsync("UPDATE parents SET kyc_status = 'verified', updated_at = NOW() WHERE identity_id = @Id",
                new { Id = identityId }, cancellationToken, tx.Transaction);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task RecordFailureAsync(Guid identityId, string provider, string? reason,
        CancellationToken cancellationToken)
    {
        await using DbTransactionScope tx = await _connectionFactory.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            "INSERT INTO identity_verifications (identity_id, provider, outcome, reason) VALUES (@Id, @Provider, 'failed', @Reason)",
            new { Id = identityId, Provider = provider, Reason = reason }, cancellationToken, tx.Transaction);

        await ExecuteAsync("UPDATE identities SET kyc_status = 'rejected', updated_at = NOW() WHERE id = @Id",
            new { Id = identityId }, cancellationToken, tx.Transaction);

        await tx.CommitAsync(cancellationToken);
    }
}
