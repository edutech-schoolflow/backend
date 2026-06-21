using EduTech.Shared.Caching;
using Microsoft.Extensions.Logging;

namespace EduTech.Shared.Features;

internal sealed class FeatureFlagService : IFeatureFlagService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const string NoOverride = "none";

    private readonly IFeatureFlagRepository _repository;
    private readonly ICacheProvider _cache;
    private readonly ILogger<FeatureFlagService> _logger;

    public FeatureFlagService(IFeatureFlagRepository repository, ICacheProvider cache,
        ILogger<FeatureFlagService> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsEnabledAsync(string key, Guid? schoolId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (schoolId is Guid id)
            {
                string overrideValue = await ResolveSchoolOverrideAsync(id, key, cancellationToken);
                if (overrideValue != NoOverride)
                {
                    return overrideValue == "1";
                }
            }

            return await ResolveGlobalAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail-closed: if the flag store is unreachable, treat the feature as OFF (and log).
            _logger.LogWarning(ex, "Feature flag '{Key}' could not be resolved; treating as disabled.", key);
            return false;
        }
    }

    public Task<IReadOnlyList<FeatureFlag>> ListAsync(CancellationToken cancellationToken = default)
    {
        return _repository.ListAsync(cancellationToken);
    }

    public Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        return _repository.EnsureSeededAsync(FeatureKeys.All, cancellationToken);
    }

    public Task InvalidateGlobalAsync(string key)
    {
        return _cache.RemoveAsync(GlobalCacheKey(key));
    }

    public Task InvalidateSchoolAsync(Guid schoolId, string key)
    {
        return _cache.RemoveAsync(SchoolCacheKey(schoolId, key));
    }

    private async Task<bool> ResolveGlobalAsync(string key, CancellationToken cancellationToken)
    {
        string cacheKey = GlobalCacheKey(key);
        string? cached = await _cache.GetAsync(cacheKey);
        if (cached is not null)
        {
            return cached == "1";
        }

        bool enabled = await _repository.GetGlobalEnabledAsync(key, cancellationToken) ?? false;
        await _cache.SetAsync(cacheKey, enabled ? "1" : "0", CacheTtl);
        return enabled;
    }

    private async Task<string> ResolveSchoolOverrideAsync(Guid schoolId, string key,
        CancellationToken cancellationToken)
    {
        string cacheKey = SchoolCacheKey(schoolId, key);
        string? cached = await _cache.GetAsync(cacheKey);
        if (cached is not null)
        {
            return cached;
        }

        bool? dbValue = await _repository.GetSchoolOverrideAsync(schoolId, key, cancellationToken);
        string value = dbValue is null ? NoOverride : (dbValue.Value ? "1" : "0");
        await _cache.SetAsync(cacheKey, value, CacheTtl);
        return value;
    }

    private static string GlobalCacheKey(string key) => $"ff:g:{key}";

    private static string SchoolCacheKey(Guid schoolId, string key) => $"ff:s:{schoolId}:{key}";
}
