using Microsoft.Extensions.Caching.Memory;

namespace EduTech.Shared.Caching;

/// <summary>
/// In-memory <see cref="ICacheProvider"/> — the fallback used when no Redis connection string is
/// configured, so local dev runs without a Redis container. NOT shared across instances.
/// </summary>
public sealed class InMemoryCacheProvider : ICacheProvider
{
    private readonly IMemoryCache _cache;

    public InMemoryCacheProvider(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task SetAsync(string key, string value, TimeSpan? expiry = null)
    {
        MemoryCacheEntryOptions options = new MemoryCacheEntryOptions();
        if (expiry is TimeSpan ttl)
        {
            options.AbsoluteExpirationRelativeToNow = ttl;
        }

        _cache.Set(key, value, options);
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key)
    {
        _cache.TryGetValue(key, out string? value);
        return Task.FromResult(value);
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }
}
