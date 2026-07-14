using EduTech.Auth.Security;
using EduTech.Auth.Unified;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
namespace EduTech.Auth.Parent;



public interface IParentProfileService
{
    Task<Guid> ProvisionAsync(string userType, Guid actorId, CancellationToken cancellationToken);

    Task<FamilyProfileResponse> GetFamilyProfileAsync(string userType, Guid actorId,
        CancellationToken cancellationToken);

    Task SetPaymentPinAsync(string userType, Guid actorId, string? pin, CancellationToken cancellationToken);
}

public sealed class FamilyProfileResponse
{
    public required bool HasProfile { get; init; }
    public string? FullName { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public bool PhoneVerified { get; init; }
    public bool HasPaymentPin { get; init; }
}

internal sealed class ParentProfileService : IParentProfileService
{
    private const int PinLength = 6;

    private readonly IParentRepository _parents;
    private readonly IAuthContextRepository _identityResolver;
    private readonly IPasswordHasher _passwordHasher;

    public ParentProfileService(IParentRepository parents, IAuthContextRepository identityResolver,
        IPasswordHasher passwordHasher)
    {
        _parents = parents;
        _identityResolver = identityResolver;
        _passwordHasher = passwordHasher;
    }

    public async Task<Guid> ProvisionAsync(string userType, Guid actorId, CancellationToken cancellationToken)
    {
        Guid identityId = await ResolveIdentityIdAsync(userType, actorId, cancellationToken);
        return await _parents.ProvisionFromIdentityAsync(identityId, cancellationToken);
    }

    public async Task<FamilyProfileResponse> GetFamilyProfileAsync(string userType, Guid actorId,
        CancellationToken cancellationToken)
    {
        Guid identityId = await ResolveIdentityIdAsync(userType, actorId, cancellationToken);
        ParentProfileRow? profile = await _parents.GetProfileByIdentityIdAsync(identityId, cancellationToken);

        if (profile is null)
        {
            return new FamilyProfileResponse { HasProfile = false };
        }

        return new FamilyProfileResponse
        {
            HasProfile = true,
            FullName = profile.FullName,
            Phone = profile.Phone,
            Email = profile.Email,
            PhoneVerified = profile.PhoneVerified,
            HasPaymentPin = profile.HasPaymentPin
        };
    }

    public async Task SetPaymentPinAsync(string userType, Guid actorId, string? pin,
        CancellationToken cancellationToken)
    {
        if (pin is null || pin.Length != PinLength || !pin.All(char.IsDigit))
        {
            throw new AppErrorException("PIN must be 6 digits.", 400, ErrorCodes.ValidationError);
        }

        Guid identityId = await ResolveIdentityIdAsync(userType, actorId, cancellationToken);
        Guid parentId = await _parents.GetIdByIdentityIdAsync(identityId, cancellationToken)
            ?? throw new AppErrorException("Set up your family profile first.", 404, ErrorCodes.NotFound,
                logReason: "Payment PIN: identity has no parent profile.");

        await _parents.SetPaymentPinAsync(parentId, _passwordHasher.Hash(pin), cancellationToken);
    }

    private async Task<Guid> ResolveIdentityIdAsync(string userType, Guid actorId,
        CancellationToken cancellationToken)
    {
        return await _identityResolver.GetIdentityIdForActorAsync(userType, actorId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);
    }
}
