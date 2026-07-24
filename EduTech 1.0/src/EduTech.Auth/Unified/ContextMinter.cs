using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
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
