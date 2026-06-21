using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace EduTech.Shared.HealthChecks;

/// <summary>Pings Redis so <c>/health</c> reports cache availability. Registered only when Redis is configured.</summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            TimeSpan latency = await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis OK ({latency.TotalMilliseconds:F0} ms)");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis unreachable", ex);
        }
    }
}
