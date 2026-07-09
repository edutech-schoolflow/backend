using EduTech.Shared.Constants;

namespace EduTech.Workforce;

/// <summary>
/// Resolves a staff member's effective 13 feature flags for one affiliation, in layers:
/// role defaults → permission template (if assigned) → per-staff overrides. Mirrors the frontend
/// computeFeatures(). Output is the full 13-flag map embedded in the scoped token.
/// </summary>
internal static class StaffFeatureResolver
{
    public static IReadOnlyDictionary<string, bool> Resolve(
        string role,
        IReadOnlyDictionary<string, bool>? templateFeatures,
        IReadOnlyDictionary<string, bool>? overrides)
    {
        IReadOnlyList<string> roleDefaults = StaffRoleFeatures.For(role);
        Dictionary<string, bool> resolved = new Dictionary<string, bool>(StaffFeatureFlags.All.Count);

        foreach (string flag in StaffFeatureFlags.All)
        {
            // Base: the template's value if a template is assigned, otherwise the role default.
            bool baseValue = templateFeatures is not null
                ? templateFeatures.TryGetValue(flag, out bool templateValue) && templateValue
                : roleDefaults.Contains(flag);

            // Per-staff override wins over the base.
            resolved[flag] = overrides is not null && overrides.TryGetValue(flag, out bool overrideValue)
                ? overrideValue
                : baseValue;
        }

        return resolved;
    }
}
