using System.Data;
using System.Diagnostics;
using Dapper;
using EduTech.Shared.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EduTech.Shared.HealthChecks;

/// <summary>
/// Runs <c>SELECT 1</c> so <c>/health</c> reports database availability — the primary dependency. Always
/// registered (unlike Redis, the DB is mandatory), so an orchestrator's readiness probe fails when Postgres
/// is unreachable instead of routing traffic to an instance that can't serve requests.
/// </summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PostgresHealthCheck(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            long started = Stopwatch.GetTimestamp();
            using IDbConnection connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: cancellationToken));
            double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return HealthCheckResult.Healthy($"Postgres OK ({ms:F0} ms)");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Postgres unreachable", ex);
        }
    }
}
