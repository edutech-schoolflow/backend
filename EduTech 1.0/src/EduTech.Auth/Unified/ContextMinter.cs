using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Persistence;
using EduTech.Workforce;

namespace EduTech.Auth.Unified;

/// <summary>The mint output: the signed context access token + the legacy refresh actor key.</summary>
internal sealed record ContextMint(AccessToken Access, string RefreshActorType, Guid RefreshActorId);

/// <summary>
/// Creates a context-scoped access token for an entered context (EDD-012). The ONE place a context token
/// is minted — used by login auto-enter, select-context, and silent refresh. Injected so the legacy and
/// canonical implementations can be swapped by DI after a claim-equivalence proof (B2d mint re-source).
/// </summary>
internal interface IContextMinter
{
    Task<ContextMint> MintAsync(EduTech.Identity.Domain.Identity identity, AuthContextItem context,
        CancellationToken cancellationToken);
}

/// <summary>
/// LEGACY context minter — mints from the legacy actor tables: owner details via
/// <c>ListOwnerContextsAsync</c> (school_owners), staff role/details via <c>ListStaffContextsAsync</c> +
/// <c>GetActiveForSwitchAsync</c> (staff_affiliations) + <c>GetTokenClaimsAsync</c> (staff_users). Extracted
/// verbatim from <c>UnifiedAuthService.MintContextAccessAsync</c> so a <c>CanonicalContextMinter</c> can be
/// built beside it and proven claim-equivalent before the DI flip. Behavior-identical to the prior mint.
/// </summary>
internal sealed class LegacyContextMinter : IContextMinter
{
    private readonly IAuthContextRepository _contexts;
    private readonly IStaffAffiliationRepository _affiliations;
    private readonly IStaffUserRepository _staffUsers;
    private readonly IAccessTokenIssuer _accessTokenIssuer;

    public LegacyContextMinter(IAuthContextRepository contexts, IStaffAffiliationRepository affiliations,
        IStaffUserRepository staffUsers, IAccessTokenIssuer accessTokenIssuer)
    {
        _contexts = contexts;
        _affiliations = affiliations;
        _staffUsers = staffUsers;
        _accessTokenIssuer = accessTokenIssuer;
    }

    public async Task<ContextMint> MintAsync(EduTech.Identity.Domain.Identity identity, AuthContextItem context,
        CancellationToken cancellationToken)
    {
        Guid actorId = context.Id;

        switch (context.Type)
        {
            case "owner":
            {
                IReadOnlyList<OwnerContextRow> owners =
                    await _contexts.ListOwnerContextsAsync(identity.Id, cancellationToken);
                OwnerContextRow owner = owners.First(o => o.OwnerId == actorId);
                AccessToken access = _accessTokenIssuer.IssueSchoolOwner(owner.OwnerId, owner.SchoolId,
                    identity.Phone, owner.Status, owner.KycStatus, owner.Subdomain,
                    identityId: identity.Id, contextId: owner.OwnerId,
                    membershipId: context.MembershipId, organizationId: context.OrganizationId);
                return new ContextMint(access, AuthActorTypes.SchoolOwner, owner.OwnerId);
            }

            case "staff":
            {
                IReadOnlyList<StaffContextRow> staffContexts =
                    await _contexts.ListStaffContextsAsync(identity.Id, cancellationToken);
                StaffContextRow staffContext = staffContexts.First(s => s.AffiliationId == actorId);

                StaffSwitchRow affiliation = await _affiliations.GetActiveForSwitchAsync(
                        staffContext.StaffUserId, staffContext.SchoolId, cancellationToken)
                    ?? throw new AppErrorException("You don't have an active role at this school.",
                        403, ErrorCodes.Forbidden);
                StaffUserTokenRow staff = await _staffUsers.GetTokenClaimsAsync(staffContext.StaffUserId, cancellationToken)
                    ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);

                AccessToken access = _accessTokenIssuer.IssueStaffScoped(staffContext.StaffUserId,
                    staffContext.SchoolId, affiliation.AffiliationId, identity.Phone,
                    affiliation.Role, affiliation.EmploymentType, staff.KycStatus,
                    identityId: identity.Id, contextId: affiliation.AffiliationId,
                    membershipId: context.MembershipId, organizationId: context.OrganizationId);
                return new ContextMint(access, AuthActorTypes.Staff, staffContext.StaffUserId);
            }

            case "parent":
            {
                // Org-scoped parent context (EDD-002 revision): the token carries the school so parent
                // data binds @SchoolId + @ParentId. A legacy NULL-org context stays school-agnostic.
                AccessToken access = _accessTokenIssuer.IssueParent(actorId, identity.Phone,
                    identityId: identity.Id, contextId: actorId, schoolId: context.OrganizationId,
                    membershipId: context.MembershipId, organizationId: context.OrganizationId);
                return new ContextMint(access, AuthActorTypes.Parent, actorId);
            }

            default:
                throw new AppErrorException("Unknown context.", 400, ErrorCodes.ValidationError);
        }
    }
}

/// <summary>
/// CANONICAL context minter (EDD-012 / EDD-015 B2d) — mints from the canonical graph, honouring the mint
/// invariant: <b>no canonical claim originates from a legacy actor table.</b>
/// <list type="bullet">
///   <item><b>owner</b> — owner_id = <c>context.Id</c>, school = <c>context.OrganizationId</c>; no
///     <c>school_owners</c> read (status/kyc/subdomain were retired in B2c.3d).</item>
///   <item><b>staff</b> — role from active <c>Employment → Position.slug</c> (no <c>staff_affiliations</c>);
///     <c>user_id</c> = <c>staff_users.id</c> by identity — a COMPATIBILITY lookup (no longer an authority),
///     retiring with the <c>user_id</c> claim.</item>
///   <item><b>parent</b> — unchanged (never read a legacy table).</item>
/// </list>
/// Proven claim-equivalent to <see cref="LegacyContextMinter"/> by the mint equivalence harness before the
/// DI flip. Same tokens, no legacy actor authority.
/// </summary>
internal sealed class CanonicalContextMinter : BaseRepository, IContextMinter
{
    private readonly IAccessTokenIssuer _accessTokenIssuer;

    public CanonicalContextMinter(IDbConnectionFactory connectionFactory, IAccessTokenIssuer accessTokenIssuer)
        : base(connectionFactory)
    {
        _accessTokenIssuer = accessTokenIssuer;
    }

    public async Task<ContextMint> MintAsync(EduTech.Identity.Domain.Identity identity, AuthContextItem context,
        CancellationToken cancellationToken)
    {
        Guid actorId = context.Id;                          // = reference_id (context_id claim, unchanged)
        Guid organizationId = context.OrganizationId ?? Guid.Empty;

        switch (context.Type)
        {
            case "owner":
            {
                // owner_id and school both come from the context — school_owners is not read.
                AccessToken access = _accessTokenIssuer.IssueSchoolOwner(actorId, organizationId,
                    identity.Phone, schoolStatus: string.Empty, kycStatus: string.Empty, subdomain: null,
                    identityId: identity.Id, contextId: actorId,
                    membershipId: context.MembershipId, organizationId: context.OrganizationId);
                return new ContextMint(access, AuthActorTypes.SchoolOwner, actorId);
            }

            case "staff":
            {
                // user_id: the staff_user for this identity — a compatibility lookup, not an authority.
                Guid staffUserId = await QuerySingleOrDefaultAsync<Guid>(
                    "SELECT id FROM staff_users WHERE identity_id = @Id", new { Id = identity.Id }, cancellationToken);

                // role: the active Employment's Position slug — canonical, no staff_affiliations.
                string? role = context.MembershipId is Guid membershipId
                    ? await QuerySingleOrDefaultAsync<string>(
                        """
                        SELECT p.slug FROM employments e JOIN positions p ON p.id = e.position_id
                        WHERE e.membership_id = @MembershipId AND e.status = 'active'
                        ORDER BY e.started_at NULLS LAST, e.created_at
                        LIMIT 1
                        """,
                        new { MembershipId = membershipId }, cancellationToken)
                    : null;

                AccessToken access = _accessTokenIssuer.IssueStaffScoped(staffUserId, organizationId, actorId,
                    identity.Phone, role ?? string.Empty, employmentType: string.Empty, kycStatus: string.Empty,
                    identityId: identity.Id, contextId: actorId,
                    membershipId: context.MembershipId, organizationId: context.OrganizationId);
                return new ContextMint(access, AuthActorTypes.Staff, staffUserId);
            }

            case "parent":
            {
                AccessToken access = _accessTokenIssuer.IssueParent(actorId, identity.Phone,
                    identityId: identity.Id, contextId: actorId, schoolId: context.OrganizationId,
                    membershipId: context.MembershipId, organizationId: context.OrganizationId);
                return new ContextMint(access, AuthActorTypes.Parent, actorId);
            }

            default:
                throw new AppErrorException("Unknown context.", 400, ErrorCodes.ValidationError);
        }
    }
}
