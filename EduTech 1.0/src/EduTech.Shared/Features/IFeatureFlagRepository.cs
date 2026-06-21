using System.Data;

namespace EduTech.Shared.Features;

/// <summary>
/// Data access for the global <c>feature_flags</c> and per-school <c>school_feature_flags</c> tables.
/// Public so the Platform Admin module can enlist writes in its audit transaction. Each write helper
/// takes an optional <see cref="IDbTransaction"/> (CardService unit-of-work pattern).
/// </summary>
public interface IFeatureFlagRepository
{
    /// <summary>Global enabled value, or null if the flag doesn't exist.</summary>
    Task<bool?> GetGlobalEnabledAsync(string key, CancellationToken cancellationToken);

    /// <summary>Per-school override, or null if no override exists for (school, key).</summary>
    Task<bool?> GetSchoolOverrideAsync(Guid schoolId, string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeatureFlag>> ListAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    Task CreateAsync(string key, string? description, bool enabled,
        IDbTransaction? transaction, CancellationToken cancellationToken);

    Task SetGlobalAsync(string key, bool enabled,
        IDbTransaction? transaction, CancellationToken cancellationToken);

    Task SetSchoolOverrideAsync(Guid schoolId, string key, bool enabled,
        IDbTransaction? transaction, CancellationToken cancellationToken);

    Task ClearSchoolOverrideAsync(Guid schoolId, string key,
        IDbTransaction? transaction, CancellationToken cancellationToken);

    /// <summary>Idempotently inserts the known keys (default disabled). Run at startup.</summary>
    Task EnsureSeededAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken);
}
