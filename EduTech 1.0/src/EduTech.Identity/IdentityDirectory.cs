using EduTech.Shared.Phone;

namespace EduTech.Identity;

/// <summary>What a lookup reveals about a person — enough for "link vs create", nothing more.</summary>
public sealed class IdentityLookup
{
    public required string FullName { get; init; }
    /// <summary>True once the person has set a password (registered); false = pending/school-seeded.</summary>
    public required bool IsClaimed { get; init; }
}

/// <summary>
/// The Identity context's public read API for other contexts (EDD-002 V3): who exists at this phone?
/// Consumers (e.g. Admissions' add-student modal) ask here instead of reading people tables directly.
/// </summary>
public interface IIdentityDirectory
{
    /// <summary>Null when no identity exists for the phone. Throws 400-style on unparseable phone? No —
    /// returns null; input validation is the caller's UX concern.</summary>
    Task<IdentityLookup?> LookupByPhoneAsync(string? rawPhone, CancellationToken cancellationToken);
}

internal sealed class IdentityDirectory : IIdentityDirectory
{
    private readonly IIdentityRepository _identities;

    public IdentityDirectory(IIdentityRepository identities)
    {
        _identities = identities;
    }

    public async Task<IdentityLookup?> LookupByPhoneAsync(string? rawPhone, CancellationToken cancellationToken)
    {
        string? phone = PhoneNumber.Normalize(rawPhone);
        if (phone is null)
        {
            return null;
        }

        Domain.Identity? identity = await _identities.GetByPhoneAsync(phone, cancellationToken);
        if (identity is null)
        {
            return null;
        }

        return new IdentityLookup
        {
            FullName = string.Join(' ', new[] { identity.FirstName, identity.MiddleName, identity.LastName }
                .Where(n => !string.IsNullOrWhiteSpace(n))),
            IsClaimed = identity.IsClaimed
        };
    }
}
