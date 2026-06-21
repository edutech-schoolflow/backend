using System.Data;
using Npgsql;

namespace EduTech.Shared.Persistence;

/// <summary>
/// PostgreSQL connection factory. The schema relies on Postgres features that the
/// design depends on — partial unique indexes (full-time staff exclusivity,
/// single-active-enrollment), JSONB, gen_random_uuid(), TIMESTAMPTZ.
/// </summary>
public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }

    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
    public async Task<DbTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        return new DbTransactionScope(connection, transaction);
    }
}
