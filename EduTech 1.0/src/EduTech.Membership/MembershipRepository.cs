using EduTech.Membership.Domain;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Persistence;

namespace EduTech.Membership;

/// <summary>
/// Data access for the canonical <c>memberships</c> belonging edge (EDD-007). This is where adult
/// membership lifecycle writes live — moved out of <c>EduTech.Auth</c> so authentication consumes
/// the edge rather than owning it.
///
/// Non-tenant: memberships are keyed by identity + organization and authorized by that ownership,
/// so this derives from <see cref="BaseRepository"/> (like the identities store). The table column
/// is still <c>school_id</c>; the FK re-points to <c>organizations</c> in a later strangler sprint
/// (after the Organization root from EDD-010 has been stable).
/// </summary>
internal interface IMembershipRepository
{
    /// <summary>
    /// Idempotently records that an identity belongs to an organization in the given capacity, and
    /// (re)activates it. Safe to call repeatedly — the DB unique constraint (identity, org, kind)
    /// makes it an upsert.
    /// </summary>
    Task EnsureActiveAsync(Guid identityId, Guid organizationId, string kind, CancellationToken cancellationToken);

    /// <summary>Ends an active membership (idempotent — ending an absent/ended edge is a no-op).</summary>
    Task EndAsync(Guid identityId, Guid organizationId, string kind, CancellationToken cancellationToken);

    /// <summary>The identity's active belonging edges — the canonical read.</summary>
    Task<IReadOnlyList<Domain.Membership>> ListActiveForIdentityAsync(Guid identityId, CancellationToken cancellationToken);
}

internal sealed class MembershipRow
{
    public Guid Id { get; init; }
    public Guid IdentityId { get; init; }
    public Guid OrganizationId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Status { get; init; } = "active";
    public DateTime JoinedAt { get; init; }
    public DateTime? EndedAt { get; init; }
}

internal sealed class MembershipRepository : BaseRepository, IMembershipRepository
{
    public MembershipRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task EnsureActiveAsync(Guid identityId, Guid organizationId, string kind,
        CancellationToken cancellationToken)
    {
        GuardKind(kind);
        return ExecuteAsync(
            """
            INSERT INTO memberships (identity_id, school_id, kind)
            VALUES (@IdentityId, @OrganizationId, @Kind)
            ON CONFLICT (identity_id, school_id, kind) DO UPDATE SET status = 'active', ended_at = NULL
            """,
            new { IdentityId = identityId, OrganizationId = organizationId, Kind = kind }, cancellationToken);
    }

    public Task EndAsync(Guid identityId, Guid organizationId, string kind, CancellationToken cancellationToken)
    {
        GuardKind(kind);
        return ExecuteAsync(
            """
            UPDATE memberships
               SET status = 'ended', ended_at = NOW()
             WHERE identity_id = @IdentityId AND school_id = @OrganizationId AND kind = @Kind
               AND status = 'active'
            """,
            new { IdentityId = identityId, OrganizationId = organizationId, Kind = kind }, cancellationToken);
    }

    public async Task<IReadOnlyList<Domain.Membership>> ListActiveForIdentityAsync(Guid identityId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MembershipRow> rows = await QueryAsync<MembershipRow>(
            """
            SELECT id, identity_id AS IdentityId, school_id AS OrganizationId, kind,
                   status, joined_at AS JoinedAt, ended_at AS EndedAt
            FROM memberships
            WHERE identity_id = @IdentityId AND status = 'active'
            ORDER BY joined_at
            """,
            new { IdentityId = identityId }, cancellationToken);

        return rows.Select(Rehydrate).ToList();
    }

    private static void GuardKind(string kind)
    {
        if (!MembershipKind.IsValid(kind))
        {
            throw new AppErrorException("Unknown membership kind.", 400, ErrorCodes.ValidationError,
                logReason: $"Membership write with invalid kind '{kind}'.");
        }
    }

    private static Domain.Membership Rehydrate(MembershipRow r) => new Domain.Membership(
        r.Id, r.IdentityId, r.OrganizationId, r.Kind,
        r.Status == "ended" ? MembershipStatus.Ended : MembershipStatus.Active,
        r.JoinedAt, r.EndedAt);
}
