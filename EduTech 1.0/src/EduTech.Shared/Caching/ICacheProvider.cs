namespace EduTech.Shared.Caching;

/// <summary>
/// Cross-cutting cache abstraction so modules never couple to a backing store. Backed by
/// <c>RedisCacheProvider</c> when Redis is configured, otherwise an in-memory fallback.
/// </summary>
public interface ICacheProvider
{
    Task SetAsync(string key, string value, TimeSpan? expiry = null);
    Task<string?> GetAsync(string key);
    Task RemoveAsync(string key);
}
