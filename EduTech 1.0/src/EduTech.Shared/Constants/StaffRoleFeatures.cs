using EduTech.Shared.Authorization;

namespace EduTech.Shared.Constants;

/// <summary>
/// LEGACY (EDD-006). The default feature flags per staff role — now a <b>projection</b> of the
/// canonical <see cref="RoleCapabilities"/> onto the 13 legacy feature flags. It is no longer a
/// source of truth: to change what a role can do, edit <see cref="RoleCapabilities"/>. This shim
/// exists only so the flag-based resolver and token minting keep working until the JWT is slimmed
/// (Sprint C), after which it can be deleted.
/// </summary>
public static class StaffRoleFeatures
{
    /// <summary>
    /// The default enabled-flag set for a role — the role's capabilities projected to their legacy
    /// flags (capabilities without a legacy flag are omitted). Empty if the role is unknown.
    /// </summary>
    public static IReadOnlyList<string> For(string role) =>
        RoleCapabilities.For(role)
            .Select(CapabilityRegistry.LegacyFlagFor)
            .Where(flag => flag is not null)
            .Select(flag => flag!)
            .ToArray();
}
