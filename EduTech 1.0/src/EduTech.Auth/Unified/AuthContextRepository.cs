using EduTech.Shared.Persistence;

namespace EduTech.Auth.Unified;

/// <summary>
/// Reads an identity's organization relationships (contexts) from the legacy silos via their
/// <c>identity_id</c> links (migration 0036). When Membership/Employment become first-class tables
/// (EDD-001 Sprint 4) this repository re-points at them — the service above doesn't change.
/// </summary>
internal interface IAuthContextRepository
{
    /// <summary>
    /// The identity's ACTIVE access contexts from the projection (EDD-003 AccessContext) — the one
    /// query login needs; org names joined for display, staff role joined for the picker subtitle.
    /// </summary>
    Task<IReadOnlyList<AccessContextRow>> ListAccessContextsAsync(Guid identityId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OwnerContextRow>> ListOwnerContextsAsync(Guid identityId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffContextRow>> ListStaffContextsAsync(Guid identityId, CancellationToken cancellationToken);
    Task<ParentContextRow?> GetParentContextAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>Links a (possibly pre-existing) parent profile to its identity; no-op if already linked.</summary>
    Task LinkParentAsync(Guid parentId, Guid identityId, CancellationToken cancellationToken);

    /// <summary>Per-context last activity (latest refresh-token issue for its actor) — SELECT-only.
    /// Entering a workspace mints a refresh token, so issuance time IS the "last entered" signal.</summary>
    Task<IReadOnlyList<ContextRecencyRow>> ListContextRecencyAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>Family counts for the identity home — SELECT-only projection (EDD-005).</summary>
    Task<FamilySummaryRow> GetFamilySummaryAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>Profile kinds this identity owns ("parent", "staff") — distinct from contexts.</summary>
    Task<IReadOnlyList<string>> ListProfileKindsAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>Resolves a legacy portal actor (owner/staff/parent id) to its identity, if linked.</summary>
    Task<Guid?> GetIdentityIdForActorAsync(string userType, Guid actorId, CancellationToken cancellationToken);

    /// <summary>The organization behind a workspace URL — /o/{slug} (FE-001 Phase 2).</summary>
    Task<OrganizationRow?> GetOrganizationBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>True if another school already holds this slug (the caller's own is excluded).</summary>
    Task<bool> SlugTakenAsync(string slug, Guid exceptSchoolId, CancellationToken cancellationToken);

    /// <summary>The Organization Wizard's write: names the school, sets its type/state, and re-slugs.</summary>
    Task SetOrganizationDetailsAsync(Guid schoolId, string name, string? type, string? state, string slug,
        CancellationToken cancellationToken);

    /// <summary>Unaccepted, unexpired staff invites addressed to this phone (adaptive /welcome).</summary>
    Task<IReadOnlyList<PendingInviteRow>> ListPendingInvitesByPhoneAsync(string phone, CancellationToken cancellationToken);

    /// <summary>Bootstrapped-but-unnamed organizations this identity owns (adaptive /welcome resume).</summary>
    Task<IReadOnlyList<DraftOrganizationRow>> ListDraftOrganizationsAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>Registration/verification of a school owner keeps their identity in step (create-or-claim,
    /// link identity_id + the 'owner' position). Idempotent — mirrors the staff variant below. Returns
    /// the linked identity + school so the caller can drive the 'owner' membership (EDD-007).</summary>
    Task<OwnerIdentityLink> EnsureOwnerIdentityLinksAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Owner phone verified → the identity's phone is verified too (unified login gates on it).</summary>
    Task MarkOwnerIdentityVerifiedAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// After an invite is accepted: ensures an identity exists for the staff member (created from the
    /// staff row if the phone is new), and links staff_users.identity_id + the affiliation's
    /// identity_id/position_id. Idempotent. Returns the linked identity + full name so the caller can
    /// drive the 'staff' membership (EDD-007) and publish the employment event.
    /// </summary>
    Task<StaffIdentityLink> EnsureStaffIdentityLinksAsync(Guid staffUserId, Guid affiliationId, CancellationToken cancellationToken);
}

/// <summary>The identity + school linked to a school owner — what the caller needs to drive the owner membership.</summary>
internal sealed record OwnerIdentityLink(Guid? IdentityId, Guid SchoolId);

/// <summary>The identity + display name linked to a staff member.</summary>
internal sealed record StaffIdentityLink(Guid? IdentityId, string Name);

internal sealed class ContextRecencyRow
{
    public Guid ContextId { get; init; }
    public DateTime? LastActiveAt { get; init; }
}

internal sealed class FamilySummaryRow
{
    public int Children { get; init; }
    public int OpenApplications { get; init; }
}

internal sealed class AccessContextRow
{
    public Guid ReferenceId { get; init; }
    public string Type { get; init; } = string.Empty;   // owner | staff | parent
    public Guid? OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public string? OrganizationSlug { get; init; }
    public string? Role { get; init; }                  // staff only
}

internal sealed class OwnerContextRow
{
    public Guid OwnerId { get; init; }
    public Guid SchoolId { get; init; }
    public string? SchoolName { get; init; }
    public string Status { get; init; } = string.Empty;
    public string KycStatus { get; init; } = string.Empty;
    public string? Subdomain { get; init; }
}

internal sealed class StaffContextRow
{
    public Guid StaffUserId { get; init; }
    public Guid AffiliationId { get; init; }
    public Guid SchoolId { get; init; }
    public string? SchoolName { get; init; }
    public string Role { get; init; } = string.Empty;
}

internal sealed class ParentContextRow
{
    public Guid ParentId { get; init; }
}

internal sealed class OrganizationRow
{
    public Guid Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? LogoUrl { get; init; }
    public string Status { get; init; } = string.Empty;
    public string KycStatus { get; init; } = string.Empty;
}

internal sealed class PendingInviteRow
{
    public string? SchoolName { get; init; }
    public string Role { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}

internal sealed class DraftOrganizationRow
{
    public Guid ContextId { get; init; }        // owner reference — what select-context takes
    public Guid OrganizationId { get; init; }
    public string Slug { get; init; } = string.Empty;
}

internal sealed class AuthContextRepository : BaseRepository, IAuthContextRepository
{
    public AuthContextRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<IReadOnlyList<ContextRecencyRow>> ListContextRecencyAsync(Guid identityId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<ContextRecencyRow>(
            """
            SELECT ac.reference_id AS ContextId,
                   MAX(rt.issued_at) AS LastActiveAt
            FROM access_contexts ac
            LEFT JOIN refresh_tokens rt
              ON (ac.type = 'owner'  AND rt.actor_type = 'school_owner' AND rt.actor_id = ac.reference_id)
              OR (ac.type = 'parent' AND rt.actor_type = 'parent'       AND rt.actor_id = ac.reference_id)
              OR (ac.type = 'staff'  AND rt.actor_type = 'staff'
                  AND rt.actor_id = (SELECT sa.staff_user_id FROM staff_affiliations sa WHERE sa.id = ac.reference_id))
            WHERE ac.identity_id = @IdentityId AND ac.status = 'active'
            GROUP BY ac.reference_id
            """,
            new { IdentityId = identityId }, cancellationToken);
    }

    public async Task<FamilySummaryRow> GetFamilySummaryAsync(Guid identityId, CancellationToken cancellationToken)
    {
        return await QuerySingleOrDefaultAsync<FamilySummaryRow>(
            """
            SELECT
                (SELECT COUNT(*) FROM parent_children pc
                   JOIN parents p ON p.id = pc.parent_id
                  WHERE p.identity_id = @IdentityId AND p.is_active = TRUE) AS Children,
                (SELECT COUNT(*) FROM applications a
                   JOIN parents p ON p.id = a.parent_id
                  WHERE p.identity_id = @IdentityId
                    AND a.status NOT IN ('admitted', 'rejected', 'withdrawn')) AS OpenApplications
            """,
            new { IdentityId = identityId }, cancellationToken) ?? new FamilySummaryRow();
    }

    public Task<IReadOnlyList<AccessContextRow>> ListAccessContextsAsync(Guid identityId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<AccessContextRow>(
            """
            SELECT ac.reference_id AS ReferenceId, ac.type AS Type, ac.organization_id AS OrganizationId,
                   s.name AS OrganizationName, s.slug AS OrganizationSlug, a.role AS Role
            FROM access_contexts ac
            LEFT JOIN schools s ON s.id = ac.organization_id
            LEFT JOIN staff_affiliations a ON ac.type = 'staff' AND a.id = ac.reference_id
            WHERE ac.identity_id = @IdentityId AND ac.status = 'active'
            ORDER BY CASE ac.type WHEN 'owner' THEN 0 WHEN 'staff' THEN 1 ELSE 2 END, s.name
            """,
            new { IdentityId = identityId }, cancellationToken);
    }

    public Task<IReadOnlyList<OwnerContextRow>> ListOwnerContextsAsync(Guid identityId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<OwnerContextRow>(
            """
            SELECT o.id AS OwnerId, o.school_id AS SchoolId, s.name AS SchoolName,
                   s.status AS Status, s.kyc_status AS KycStatus, s.subdomain AS Subdomain
            FROM school_owners o
            JOIN schools s ON s.id = o.school_id
            WHERE o.identity_id = @IdentityId AND o.is_active = TRUE
            ORDER BY s.name
            """,
            new { IdentityId = identityId }, cancellationToken);
    }

    public Task<IReadOnlyList<StaffContextRow>> ListStaffContextsAsync(Guid identityId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<StaffContextRow>(
            """
            SELECT su.id AS StaffUserId, a.id AS AffiliationId, a.school_id AS SchoolId,
                   s.name AS SchoolName, a.role AS Role
            FROM staff_users su
            JOIN staff_affiliations a ON a.staff_user_id = su.id AND a.status = 'active'
            JOIN schools s ON s.id = a.school_id
            WHERE su.identity_id = @IdentityId AND su.is_active = TRUE
            ORDER BY s.name
            """,
            new { IdentityId = identityId }, cancellationToken);
    }

    public Task<ParentContextRow?> GetParentContextAsync(Guid identityId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ParentContextRow>(
            "SELECT id AS ParentId FROM parents WHERE identity_id = @IdentityId AND is_active = TRUE",
            new { IdentityId = identityId }, cancellationToken);
    }

    public Task LinkParentAsync(Guid parentId, Guid identityId, CancellationToken cancellationToken)
    {
        // Links the parent profile to its identity. Parent AccessContexts are now organization-scoped
        // (EDD-002 revision) and created per-school by EnsureParentMembershipAsync, so this no longer
        // writes a school-agnostic context row.
        return ExecuteAsync(
            "UPDATE parents SET identity_id = @IdentityId, updated_at = NOW() WHERE id = @ParentId AND identity_id IS NULL",
            new { ParentId = parentId, IdentityId = identityId }, cancellationToken);
    }

    public Task<Guid?> GetIdentityIdForActorAsync(string userType, Guid actorId, CancellationToken cancellationToken)
    {
        string sql = userType switch
        {
            // Identity-scope tokens carry the identity id itself.
            "identity" => "SELECT id FROM identities WHERE id = @Id",
            "school" => "SELECT identity_id FROM school_owners WHERE id = @Id",
            "staff"  => "SELECT identity_id FROM staff_users   WHERE id = @Id",
            "parent" => "SELECT identity_id FROM parents       WHERE id = @Id",
            _ => throw new ArgumentOutOfRangeException(nameof(userType), userType, "Unknown portal actor type."),
        };
        return QuerySingleOrDefaultAsync<Guid?>(sql, new { Id = actorId }, cancellationToken);
    }

    public Task<OrganizationRow?> GetOrganizationBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<OrganizationRow>(
            """
            SELECT id AS Id, slug AS Slug, name AS Name, logo_url AS LogoUrl,
                   status AS Status, kyc_status AS KycStatus
            FROM schools WHERE slug = @Slug
            """,
            new { Slug = slug }, cancellationToken);
    }

    public async Task<bool> SlugTakenAsync(string slug, Guid exceptSchoolId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM schools WHERE slug = @Slug AND id <> @ExceptId",
            new { Slug = slug, ExceptId = exceptSchoolId }, cancellationToken) > 0;
    }

    public Task SetOrganizationDetailsAsync(Guid schoolId, string name, string? type, string? state, string slug,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE schools
               SET name = @Name, type = @Type, state = @State, slug = @Slug, updated_at = NOW()
             WHERE id = @Id
            """,
            new { Id = schoolId, Name = name, Type = type, State = state, Slug = slug }, cancellationToken);
    }

    public Task<IReadOnlyList<PendingInviteRow>> ListPendingInvitesByPhoneAsync(string phone,
        CancellationToken cancellationToken)
    {
        return QueryAsync<PendingInviteRow>(
            """
            SELECT s.name AS SchoolName, a.role AS Role, t.expires_at AS ExpiresAt
            FROM staff_invite_tokens t
            JOIN staff_affiliations a ON a.id = t.affiliation_id
            JOIN schools s ON s.id = a.school_id
            WHERE t.phone = @Phone AND t.used_at IS NULL AND t.expires_at > NOW()
            ORDER BY t.created_at DESC
            """,
            new { Phone = phone }, cancellationToken);
    }

    public Task<IReadOnlyList<DraftOrganizationRow>> ListDraftOrganizationsAsync(Guid identityId,
        CancellationToken cancellationToken)
    {
        return QueryAsync<DraftOrganizationRow>(
            """
            SELECT ac.reference_id AS ContextId, ac.organization_id AS OrganizationId, s.slug AS Slug
            FROM access_contexts ac
            JOIN schools s ON s.id = ac.organization_id
            WHERE ac.identity_id = @IdentityId AND ac.type = 'owner' AND ac.status = 'active'
              AND s.name IS NULL
            ORDER BY s.created_at DESC
            """,
            new { IdentityId = identityId }, cancellationToken);
    }

    public async Task<OwnerIdentityLink> EnsureOwnerIdentityLinksAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        // The 'owner' membership is now driven by the caller via the Membership context (EDD-007);
        // this method owns only the identity link + the access_contexts projection.
        await ExecuteAsync(
            """
            INSERT INTO identities (first_name, middle_name, last_name, phone, password_hash,
                                    phone_verified, email_verified, status)
            SELECT o.first_name, o.middle_name, o.last_name, o.phone, o.password_hash,
                   o.phone_verified, o.email_verified,
                   CASE WHEN o.password_hash IS NOT NULL THEN 'active' ELSE 'pending' END
            FROM school_owners o
            WHERE o.id = @OwnerId
              AND NOT EXISTS (SELECT 1 FROM identities i WHERE i.phone = o.phone)
            ON CONFLICT (phone) DO NOTHING;

            UPDATE school_owners o SET identity_id = i.id
            FROM identities i
            WHERE o.id = @OwnerId AND o.identity_id IS NULL AND i.phone = o.phone;

            -- Pre-existing PENDING identity (e.g. school-seeded guardian): the owner registration set
            -- a password — claim the identity with it. Guarded like every claim.
            UPDATE identities i
               SET password_hash = o.password_hash, updated_at = NOW()
            FROM school_owners o
            WHERE o.id = @OwnerId AND i.phone = o.phone
              AND i.password_hash IS NULL AND o.password_hash IS NOT NULL;

            UPDATE school_owners o SET position_id = p.id
            FROM positions p
            WHERE o.id = @OwnerId AND o.position_id IS NULL AND p.school_id IS NULL AND p.slug = 'owner';
            """,
            new { OwnerId = ownerId }, cancellationToken);
        // The owner access_context is projected by AccessContextProjector (EDD-012 B2a); this method
        // now only keeps the identity link + position.

        return await QuerySingleOrDefaultAsync<OwnerIdentityLink>(
            "SELECT identity_id AS IdentityId, school_id AS SchoolId FROM school_owners WHERE id = @OwnerId",
            new { OwnerId = ownerId }, cancellationToken)
            ?? new OwnerIdentityLink(null, Guid.Empty);
    }

    public Task MarkOwnerIdentityVerifiedAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE identities i
               SET phone_verified = TRUE,
                   status = CASE WHEN i.password_hash IS NOT NULL AND i.status = 'pending' THEN 'active' ELSE i.status END,
                   updated_at = NOW()
            FROM school_owners o
            WHERE o.id = @OwnerId AND i.id = o.identity_id
            """,
            new { OwnerId = ownerId }, cancellationToken);
    }

    public async Task<StaffIdentityLink> EnsureStaffIdentityLinksAsync(Guid staffUserId, Guid affiliationId,
        CancellationToken cancellationToken)
    {
        // Email intentionally omitted on identity creation here (identities.email is unique and the
        // person may claim it via unified registration); phone is the key. Multi-statement + idempotent.
        await ExecuteAsync(
            """
            INSERT INTO identities (first_name, middle_name, last_name, phone, password_hash,
                                    phone_verified, email_verified, status)
            SELECT su.first_name, su.middle_name, su.last_name, su.phone, su.password_hash,
                   su.phone_verified, su.email_verified,
                   CASE WHEN su.password_hash IS NOT NULL THEN 'active' ELSE 'pending' END
            FROM staff_users su
            WHERE su.id = @StaffUserId
              AND NOT EXISTS (SELECT 1 FROM identities i WHERE i.phone = su.phone)
            ON CONFLICT (phone) DO NOTHING;

            UPDATE staff_users su SET identity_id = i.id
            FROM identities i
            WHERE su.id = @StaffUserId AND su.identity_id IS NULL AND i.phone = su.phone;

            -- The person may pre-exist as a PENDING identity (e.g. school-seeded guardian). Their
            -- invite acceptance just set a password + verified the phone via OTP — claim the
            -- identity with it so the unified login works immediately. Guarded like every claim.
            UPDATE identities i
               SET password_hash = su.password_hash,
                   phone_verified = TRUE,
                   status = 'active',
                   updated_at = NOW()
            FROM staff_users su
            WHERE su.id = @StaffUserId AND i.phone = su.phone
              AND i.password_hash IS NULL AND su.password_hash IS NOT NULL;

            UPDATE staff_affiliations a SET identity_id = su.identity_id
            FROM staff_users su
            WHERE a.id = @AffiliationId AND su.id = @StaffUserId AND a.identity_id IS NULL;

            UPDATE staff_affiliations a SET position_id = p.id
            FROM positions p
            WHERE a.id = @AffiliationId AND a.position_id IS NULL
              AND p.school_id IS NULL AND p.slug = a.role;
            """,
            new { StaffUserId = staffUserId, AffiliationId = affiliationId }, cancellationToken);

        // The 'staff' membership (EDD-007) and the access_context (EDD-012 B2a) are both driven by the
        // caller — via the Membership context and the AccessContextProjector. This keeps only the link.
        StaffIdentityLink? link = await QuerySingleOrDefaultAsync<StaffIdentityLink>(
            "SELECT identity_id AS IdentityId, concat_ws(' ', first_name, last_name) AS Name FROM staff_users WHERE id = @Id",
            new { Id = staffUserId }, cancellationToken);
        return link is null || string.IsNullOrWhiteSpace(link.Name)
            ? new StaffIdentityLink(link?.IdentityId, "Staff member")
            : link;
    }

    public async Task<IReadOnlyList<string>> ListProfileKindsAsync(Guid identityId, CancellationToken cancellationToken)
    {
        return await QueryAsync<string>(
            """
            SELECT 'parent' FROM parents WHERE identity_id = @Id AND is_active = TRUE
            UNION ALL
            SELECT 'staff' FROM staff_users WHERE identity_id = @Id AND is_active = TRUE
            """,
            new { Id = identityId }, cancellationToken);
    }

}
