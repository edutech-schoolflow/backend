namespace EduTech.Shared.Features;

/// <summary>A release feature flag's global state (as shown/edited in the CMS).</summary>
public sealed class FeatureFlag
{
    public required string Key { get; init; }
    public string? Description { get; init; }
    public required bool Enabled { get; init; }
}

/// <summary>A per-school override of a feature flag (pilot rollout).</summary>
public sealed class FeatureFlagOverride
{
    public required Guid SchoolId { get; init; }
    public required string Key { get; init; }
    public required bool Enabled { get; init; }
}
