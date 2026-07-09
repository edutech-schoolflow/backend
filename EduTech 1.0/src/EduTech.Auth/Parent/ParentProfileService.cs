using EduTech.Auth.Unified;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Parent;

/// <summary>Creates the parent profile for a signed-in identity (idempotent). Profile only — never a session.</summary>
public interface IParentProfileService
{
    Task<Guid> ProvisionAsync(string userType, Guid actorId, CancellationToken cancellationToken);
}

internal sealed class ParentProfileService : IParentProfileService
{
    private readonly IParentRepository _parents;
    private readonly IAuthContextRepository _identityResolver;

    public ParentProfileService(IParentRepository parents, IAuthContextRepository identityResolver)
    {
        _parents = parents;
        _identityResolver = identityResolver;
    }

    public async Task<Guid> ProvisionAsync(string userType, Guid actorId, CancellationToken cancellationToken)
    {
        Guid identityId = await _identityResolver.GetIdentityIdForActorAsync(userType, actorId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);
        return await _parents.ProvisionFromIdentityAsync(identityId, cancellationToken);
    }
}
