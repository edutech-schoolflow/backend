namespace EduTech.Shared.Features;

/// <summary>
/// Resolves release feature flags (per-school override → global default) with a Redis read-through
/// cache, and exposes cache invalidation for the CMS write path. This is what <c>[FeatureGate]</c>
/// and modules call.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>True if the feature is on for this school (or globally, when <paramref name="schoolId"/> is null).</summary>
    Task<bool> IsEnabledAsync(string key, Guid? schoolId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureFlag>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Idempotently seeds the known <see cref="FeatureKeys"/> (default OFF). Run at startup.</summary>
    Task EnsureSeededAsync(CancellationToken cancellationToken = default);

    Task InvalidateGlobalAsync(string key);
    Task InvalidateSchoolAsync(Guid schoolId, string key);
}
