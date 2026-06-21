using StackExchange.Redis;

namespace EduTech.Shared.Caching;

/// <summary>Redis-backed <see cref="ICacheProvider"/> over the shared <see cref="IConnectionMultiplexer"/>.</summary>
public sealed class RedisCacheProvider : ICacheProvider
{
    private readonly IDatabase _database;

    public RedisCacheProvider(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    public async Task SetAsync(string key, string value, TimeSpan? expiry = null)
    {
        await _database.StringSetAsync(key, value);
        if (expiry is TimeSpan ttl)
        {
            await _database.KeyExpireAsync(key, ttl);
        }
    }

    public async Task<string?> GetAsync(string key)
    {
        RedisValue value = await _database.StringGetAsync(key);
        return value.IsNull ? null : value.ToString();
    }

    public Task RemoveAsync(string key)
    {
        return _database.KeyDeleteAsync(key);
    }
}
