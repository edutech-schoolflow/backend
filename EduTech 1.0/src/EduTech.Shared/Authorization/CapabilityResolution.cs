using EduTech.Shared.Constants;

namespace EduTech.Shared.Authorization;

/// <summary>
/// The pure resolution rule behind <see cref="ICapabilityResolver"/> (EDD-013): given a context's
/// type and (for staff) its role / template / overrides, produce the effective capability set. No DB,
/// no cache, no actor branching in callers — this is the one place the mapping lives.
///
/// Byte-identical to today's decisions: the staff path mirrors <c>StaffFeatureResolver.Resolve</c>
/// (role default → template → override) and maps enabled legacy flags to capabilities via
/// <see cref="CapabilityRegistry"/>. Owner grants everything; parent/unknown grants nothing.
/// </summary>
public static class CapabilityResolution
{
    public static CapabilitySet Resolve(string? contextType, string? role,
        IReadOnlyDictionary<string, bool>? templateFeatures, IReadOnlyDictionary<string, bool> overrides) =>
        contextType switch
        {
            "owner" => CapabilitySet.All,
            "staff" => StaffCapabilities(role ?? string.Empty, templateFeatures, overrides),
            _ => CapabilitySet.Empty,
        };

#pragma warning disable CS0618 // legacy flag constants are the migration bridge
    private static CapabilitySet StaffCapabilities(string role,
        IReadOnlyDictionary<string, bool>? templateFeatures, IReadOnlyDictionary<string, bool> overrides)
    {
        IReadOnlyList<string> roleDefaults = StaffRoleFeatures.For(role);

        bool FlagEnabled(string flag)
        {
            bool baseValue = templateFeatures is not null
                ? templateFeatures.TryGetValue(flag, out bool t) && t
                : roleDefaults.Contains(flag);
            return overrides.TryGetValue(flag, out bool o) ? o : baseValue;
        }

        return CapabilitySet.Of(CapabilityRegistry.All
            .Where(c => c.LegacyFlag is not null && FlagEnabled(c.LegacyFlag))
            .Select(c => c.Key));
    }
#pragma warning restore CS0618
}
