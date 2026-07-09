using EduTech.Shared.Persistence;
using Microsoft.Extensions.Logging;

namespace EduTech.Auth.Unified;

/// <summary>
/// Daily sweep (EDD-004 rule 5): the safety net behind the identity-keeping event handlers
/// (<see cref="EnsureIdentityOnGuardianLinked"/>, invite-accept links). In-process events can be
/// lost if the process dies mid-flight, so this idempotent pass converges the links regardless:
/// every parent/staff row gets an identity, every affiliation its identity/position, and every
/// enrolled child's parent a school membership. Running it twice changes nothing.
/// </summary>
public sealed class IdentityReconciliationJob
{
    private readonly IIdentityReconciliationRepository _repository;
    private readonly ILogger<IdentityReconciliationJob> _logger;

    public IdentityReconciliationJob(IIdentityReconciliationRepository repository,
        ILogger<IdentityReconciliationJob> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        int changed = await _repository.ReconcileAsync(cancellationToken);
        if (changed > 0)
        {
            _logger.LogInformation("Identity reconciliation converged {Count} rows.", changed);
        }
    }
}

public interface IIdentityReconciliationRepository
{
    /// <summary>Runs the idempotent convergence pass; returns total rows affected.</summary>
    Task<int> ReconcileAsync(CancellationToken cancellationToken);
}

internal sealed class IdentityReconciliationRepository : BaseRepository, IIdentityReconciliationRepository
{
    public IdentityReconciliationRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        // Mirrors the 0036/0037 backfills, kept in step with them. Emails are omitted on identity
        // creation (unique) — claimed later through unified registration.
        return ExecuteAsync(
            """
            -- identities for parents that don't have one yet
            INSERT INTO identities (first_name, middle_name, last_name, phone, password_hash,
                                    phone_verified, status)
            SELECT p.first_name, p.middle_name, p.last_name, p.phone, p.password_hash, p.phone_verified,
                   CASE WHEN p.password_hash IS NOT NULL THEN 'active' ELSE 'pending' END
            FROM parents p
            WHERE p.identity_id IS NULL
              AND NOT EXISTS (SELECT 1 FROM identities i WHERE i.phone = p.phone)
            ON CONFLICT (phone) DO NOTHING;

            UPDATE parents p SET identity_id = i.id
            FROM identities i WHERE p.identity_id IS NULL AND i.phone = p.phone;

            -- identities for staff that don't have one yet
            INSERT INTO identities (first_name, middle_name, last_name, phone, password_hash,
                                    phone_verified, email_verified, status)
            SELECT su.first_name, su.middle_name, su.last_name, su.phone, su.password_hash,
                   su.phone_verified, su.email_verified,
                   CASE WHEN su.password_hash IS NOT NULL THEN 'active' ELSE 'pending' END
            FROM staff_users su
            WHERE su.identity_id IS NULL
              AND NOT EXISTS (SELECT 1 FROM identities i WHERE i.phone = su.phone)
            ON CONFLICT (phone) DO NOTHING;

            UPDATE staff_users su SET identity_id = i.id
            FROM identities i WHERE su.identity_id IS NULL AND i.phone = su.phone;

            -- employment links (identity + position from role)
            UPDATE staff_affiliations a SET identity_id = su.identity_id
            FROM staff_users su
            WHERE a.staff_user_id = su.id AND a.identity_id IS NULL AND su.identity_id IS NOT NULL;

            UPDATE staff_affiliations a SET position_id = p.id
            FROM positions p
            WHERE a.position_id IS NULL AND p.school_id IS NULL AND p.slug = a.role;

            -- AccessContext projection convergence (EDD-003): upsert actives, end the gone.
            INSERT INTO access_contexts (identity_id, type, reference_id, organization_id)
            SELECT o.identity_id, 'owner', o.id, o.school_id FROM school_owners o
            WHERE o.identity_id IS NOT NULL AND o.is_active = TRUE
            ON CONFLICT (type, reference_id) DO UPDATE SET status = 'active', updated_at = NOW();

            INSERT INTO access_contexts (identity_id, type, reference_id, organization_id)
            SELECT a.identity_id, 'staff', a.id, a.school_id FROM staff_affiliations a
            WHERE a.identity_id IS NOT NULL AND a.status = 'active'
            ON CONFLICT (type, reference_id) DO UPDATE SET status = 'active', updated_at = NOW();

            INSERT INTO access_contexts (identity_id, type, reference_id, organization_id)
            SELECT p.identity_id, 'parent', p.id, NULL FROM parents p
            WHERE p.identity_id IS NOT NULL AND p.is_active = TRUE
            ON CONFLICT (type, reference_id) DO UPDATE SET status = 'active', updated_at = NOW();

            UPDATE access_contexts ac SET status = 'ended', updated_at = NOW()
            WHERE ac.status = 'active' AND (
                (ac.type = 'owner'  AND NOT EXISTS (SELECT 1 FROM school_owners o WHERE o.id = ac.reference_id AND o.is_active = TRUE))
             OR (ac.type = 'staff'  AND NOT EXISTS (SELECT 1 FROM staff_affiliations a WHERE a.id = ac.reference_id AND a.status = 'active'))
             OR (ac.type = 'parent' AND NOT EXISTS (SELECT 1 FROM parents p WHERE p.id = ac.reference_id AND p.is_active = TRUE))
            );

            -- parent memberships from enrollments (the GuardianLinked safety net)
            INSERT INTO memberships (identity_id, school_id, kind)
            SELECT DISTINCT p.identity_id, st.school_id, 'parent'
            FROM parents p
            JOIN parent_children pc ON pc.parent_id = p.id
            JOIN students st        ON st.child_profile_id = pc.child_profile_id
            WHERE p.identity_id IS NOT NULL
            ON CONFLICT (identity_id, school_id, kind) DO NOTHING;
            """,
            null, cancellationToken);
    }
}
