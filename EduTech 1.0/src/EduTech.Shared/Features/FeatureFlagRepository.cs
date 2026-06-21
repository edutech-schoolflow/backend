using System.Data;
using EduTech.Shared.Persistence;

namespace EduTech.Shared.Features;

/// <summary>
/// <c>feature_flags</c> + <c>school_feature_flags</c> are GLOBAL (platform-managed) tables — so this
/// derives from <see cref="BaseRepository"/>, not <see cref="TenantRepository"/>.
/// </summary>
internal sealed class FeatureFlagRepository : BaseRepository, IFeatureFlagRepository
{
    public FeatureFlagRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<bool?> GetGlobalEnabledAsync(string key, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<bool?>(
            "SELECT enabled FROM feature_flags WHERE key = @Key", new { Key = key }, cancellationToken);
    }

    public Task<bool?> GetSchoolOverrideAsync(Guid schoolId, string key, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<bool?>(
            "SELECT enabled FROM school_feature_flags WHERE school_id = @SchoolId AND key = @Key",
            new { SchoolId = schoolId, Key = key }, cancellationToken);
    }

    public Task<IReadOnlyList<FeatureFlag>> ListAsync(CancellationToken cancellationToken)
    {
        return QueryAsync<FeatureFlag>(
            "SELECT key AS Key, description AS Description, enabled AS Enabled FROM feature_flags ORDER BY key",
            null, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM feature_flags WHERE key = @Key", new { Key = key }, cancellationToken);
        return count > 0;
    }

    public Task CreateAsync(string key, string? description, bool enabled,
        IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO feature_flags (key, description, enabled)
            VALUES (@Key, @Description, @Enabled)
            """,
            new { Key = key, Description = description, Enabled = enabled }, cancellationToken, transaction);
    }

    public Task SetGlobalAsync(string key, bool enabled,
        IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE feature_flags SET enabled = @Enabled, updated_at = NOW() WHERE key = @Key",
            new { Key = key, Enabled = enabled }, cancellationToken, transaction);
    }

    public Task SetSchoolOverrideAsync(Guid schoolId, string key, bool enabled,
        IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO school_feature_flags (school_id, key, enabled)
            VALUES (@SchoolId, @Key, @Enabled)
            ON CONFLICT (school_id, key) DO UPDATE SET enabled = EXCLUDED.enabled, updated_at = NOW()
            """,
            new { SchoolId = schoolId, Key = key, Enabled = enabled }, cancellationToken, transaction);
    }

    public Task ClearSchoolOverrideAsync(Guid schoolId, string key,
        IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "DELETE FROM school_feature_flags WHERE school_id = @SchoolId AND key = @Key",
            new { SchoolId = schoolId, Key = key }, cancellationToken, transaction);
    }

    public Task EnsureSeededAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO feature_flags (key, enabled)
            SELECT UNNEST(@Keys), false
            ON CONFLICT (key) DO NOTHING
            """,
            new { Keys = keys.ToArray() }, cancellationToken);
    }
}
