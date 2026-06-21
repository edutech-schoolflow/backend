using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace EduTech.Shared.Caching;

/// <summary>
/// Registers the cache seam. If a connected <see cref="IConnectionMultiplexer"/> is supplied, the
/// shared multiplexer + <see cref="RedisCacheProvider"/> are registered; otherwise an in-memory
/// fallback. The caller (Program.cs) decides Redis availability once and reuses the multiplexer for
/// rate limiting + health checks.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    public static IServiceCollection AddEduTechCaching(this IServiceCollection services, IConnectionMultiplexer? redis)
    {
        if (redis is not null)
        {
            services.AddSingleton(redis);
            services.AddSingleton<ICacheProvider, RedisCacheProvider>();
        }
        else
        {
            services.AddMemoryCache();
            services.AddSingleton<ICacheProvider, InMemoryCacheProvider>();
        }

        return services;
    }
}
