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
    private readonly IAccessContextProjector _projector;
    private readonly ILogger<IdentityReconciliationJob> _logger;

    public IdentityReconciliationJob(IIdentityReconciliationRepository repository,
        IAccessContextProjector projector, ILogger<IdentityReconciliationJob> logger)
    {
        _repository = repository;
        _projector = projector;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // 1. Converge the canonical links (identities/memberships) from the legacy silos (safety net).
        int changed = await _repository.ReconcileAsync(cancellationToken);
        // 2. Rebuild the access_contexts projection from the (now-converged) canonical aggregates.
        await _projector.RebuildAllAsync(cancellationToken);
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

            -- Parent memberships from enrollments (the GuardianLinked safety net).
            INSERT INTO memberships (identity_id, school_id, kind)
            SELECT DISTINCT p.identity_id, st.school_id, 'parent'
            FROM parents p
            JOIN parent_children pc ON pc.parent_id = p.id
            JOIN students st        ON st.child_profile_id = pc.child_profile_id
            WHERE p.identity_id IS NOT NULL
            ON CONFLICT (identity_id, school_id, kind) DO NOTHING;

            -- The access_contexts projection is rebuilt by AccessContextProjector.RebuildAllAsync,
            -- called right after this converges the canonical links (EDD-012 B2a).
            """,
            null, cancellationToken);
    }
}
