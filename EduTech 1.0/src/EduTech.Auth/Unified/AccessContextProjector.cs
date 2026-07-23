using EduTech.Shared.Persistence;

namespace EduTech.Auth.Unified;

/// <summary>
/// Projects the <c>access_contexts</c> read model from the canonical foundation (EDD-012 B2a).
/// access_contexts is a DISPOSABLE PROJECTION — it may be deleted and rebuilt at any time from
/// Identity/Membership/Employment/Organization without loss of business data. This is the single
/// producer of that table.
///
/// Which contexts exist, their status, and their organization come ONLY from canonical aggregates:
///   • owner  — an owner membership with an active employment
///   • staff  — a staff membership with an active employment
///   • parent — a parent membership (one per organization)
/// The legacy actor tables (school_owners/staff_affiliations/parents) are dereferenced solely to fill
/// <c>reference_id</c> — the login-compat pointer the reader still joins on. That last legacy read
/// disappears in B2c, when login stops reading <c>reference_id</c>. No business rules live here;
/// they belong to Membership/Employment/Organization.
///
/// Pure projection — no auth, JWT, cookies, or HTTP. Idempotent and order-independent (set-based).
/// </summary>
public interface IAccessContextProjector
{
    /// <summary>Re-projects one identity's contexts: upserts its actives, ends the ones now gone.</summary>
    Task ProjectForIdentityAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>Full rebuild across every identity (the reconciliation path + the delete/rebuild test).</summary>
    Task<int> RebuildAllAsync(CancellationToken cancellationToken);
}

internal sealed class AccessContextProjector : BaseRepository, IAccessContextProjector
{
    public AccessContextProjector(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task ProjectForIdentityAsync(Guid identityId, CancellationToken cancellationToken) =>
        ProjectAsync(identityId, cancellationToken);

    public async Task<int> RebuildAllAsync(CancellationToken cancellationToken) =>
        await ProjectAsync(null, cancellationToken);

    /// <summary>
    /// One idempotent, order-independent projection. <paramref name="identityId"/> null = all identities
    /// (full rebuild); non-null = scope to that identity. Upserts active contexts, then ends any active
    /// context whose canonical source is gone.
    /// </summary>
    private Task<int> ProjectAsync(Guid? identityId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            -- OWNER: an owner membership with an active employment. reference_id = owner_id (legacy
            -- compat); membership_id = m.id (canonical identity, B2c.1).
            INSERT INTO access_contexts (identity_id, type, reference_id, organization_id, membership_id)
            SELECT m.identity_id, 'owner', o.id, m.school_id, m.id
            FROM memberships m
            JOIN school_owners o ON o.identity_id = m.identity_id AND o.school_id = m.school_id
            WHERE m.kind = 'owner' AND m.status = 'active'
              AND (@IdentityId IS NULL OR m.identity_id = @IdentityId)
              AND EXISTS (SELECT 1 FROM employments e WHERE e.membership_id = m.id AND e.status = 'active')
            ON CONFLICT (type, reference_id, organization_id)
                DO UPDATE SET status = 'active', membership_id = EXCLUDED.membership_id, updated_at = NOW();

            -- STAFF: a staff membership with an active employment. reference_id = affiliation_id.
            INSERT INTO access_contexts (identity_id, type, reference_id, organization_id, membership_id)
            SELECT m.identity_id, 'staff', a.id, m.school_id, m.id
            FROM memberships m
            JOIN staff_affiliations a ON a.identity_id = m.identity_id AND a.school_id = m.school_id
            WHERE m.kind = 'staff' AND m.status = 'active'
              AND (@IdentityId IS NULL OR m.identity_id = @IdentityId)
              AND EXISTS (SELECT 1 FROM employments e WHERE e.membership_id = m.id AND e.status = 'active')
            ON CONFLICT (type, reference_id, organization_id)
                DO UPDATE SET status = 'active', membership_id = EXCLUDED.membership_id, updated_at = NOW();

            -- PARENT: a parent membership, one per organization. reference_id = parent_id.
            INSERT INTO access_contexts (identity_id, type, reference_id, organization_id, membership_id)
            SELECT m.identity_id, 'parent', p.id, m.school_id, m.id
            FROM memberships m
            JOIN parents p ON p.identity_id = m.identity_id AND p.is_active = TRUE
            WHERE m.kind = 'parent' AND m.status = 'active'
              AND (@IdentityId IS NULL OR m.identity_id = @IdentityId)
            ON CONFLICT (type, reference_id, organization_id)
                DO UPDATE SET status = 'active', membership_id = EXCLUDED.membership_id, updated_at = NOW();

            -- END the gone: an active context with no current canonical source.
            UPDATE access_contexts ac SET status = 'ended', updated_at = NOW()
            WHERE ac.status = 'active'
              AND (@IdentityId IS NULL OR ac.identity_id = @IdentityId)
              AND (
                (ac.type = 'owner' AND NOT EXISTS (
                    SELECT 1 FROM memberships m
                    JOIN school_owners o ON o.identity_id = m.identity_id AND o.school_id = m.school_id
                    WHERE o.id = ac.reference_id AND m.kind = 'owner' AND m.status = 'active'
                      AND EXISTS (SELECT 1 FROM employments e WHERE e.membership_id = m.id AND e.status = 'active')))
             OR (ac.type = 'staff' AND NOT EXISTS (
                    SELECT 1 FROM memberships m
                    JOIN staff_affiliations a ON a.identity_id = m.identity_id AND a.school_id = m.school_id
                    WHERE a.id = ac.reference_id AND m.kind = 'staff' AND m.status = 'active'
                      AND EXISTS (SELECT 1 FROM employments e WHERE e.membership_id = m.id AND e.status = 'active')))
             OR (ac.type = 'parent' AND NOT EXISTS (
                    SELECT 1 FROM memberships m
                    JOIN parents p ON p.identity_id = m.identity_id AND p.is_active = TRUE
                    WHERE p.id = ac.reference_id AND m.school_id = ac.organization_id
                      AND m.kind = 'parent' AND m.status = 'active'))
              );
            """,
            new { IdentityId = identityId }, cancellationToken);
}
