using EduTech.Auth.Parent;
using EduTech.Identity;
using EduTech.Shared.Events;
using Microsoft.Extensions.Logging;

namespace EduTech.Auth.Unified;

/// <summary>
/// Observer (EDD-002 V2 remediation): when a school links a guardian while admitting a student, the
/// Identity context — not Academics — ensures the person exists: a (pending) Identity for the phone,
/// the parent profile linked to it, and an active parent Membership with that school. Idempotent, so
/// repeat admissions with the same guardian are no-ops.
/// </summary>
internal sealed class EnsureIdentityOnGuardianLinked : IDomainEventHandler<GuardianLinkedEvent>
{
    private readonly IIdentityRepository _identities;
    private readonly IAuthContextRepository _contexts;
    private readonly IParentRepository _parents;
    private readonly ILogger<EnsureIdentityOnGuardianLinked> _logger;

    public EnsureIdentityOnGuardianLinked(IIdentityRepository identities, IAuthContextRepository contexts,
        IParentRepository parents, ILogger<EnsureIdentityOnGuardianLinked> logger)
    {
        _identities = identities;
        _contexts = contexts;
        _parents = parents;
        _logger = logger;
    }

    public async Task HandleAsync(GuardianLinkedEvent domainEvent, CancellationToken cancellationToken)
    {
        // Same placeholder fallback the legacy parent-seed used; the person's own name wins on claim.
        string firstName = string.IsNullOrWhiteSpace(domainEvent.FirstName) ? "Guardian" : domainEvent.FirstName;
        string lastName = string.IsNullOrWhiteSpace(domainEvent.LastName) ? firstName : domainEvent.LastName;

        Guid identityId = await _identities.EnsurePendingAsync(firstName, lastName, domainEvent.Phone,
            cancellationToken);

        if (await _parents.GetIdByPhoneAsync(domainEvent.Phone, cancellationToken) is Guid parentId)
        {
            await _contexts.LinkParentAsync(parentId, identityId, cancellationToken);
        }

        await _contexts.EnsureParentMembershipAsync(identityId, domainEvent.SchoolId, cancellationToken);

        _logger.LogInformation("Guardian {Phone} ensured as identity {IdentityId} with membership at {SchoolId}.",
            domainEvent.Phone, identityId, domainEvent.SchoolId);
    }
}
