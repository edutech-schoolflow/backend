using System.Data;

namespace EduTech.Shared.Persistence;

/// <summary>
/// Creates database connections for Dapper. Backed by PostgreSQL (Npgsql).
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates a new closed connection. Dapper opens it automatically per command,
    /// so this is the right choice for single-statement reads/writes.
    /// </summary>
    IDbConnection CreateConnection();

    /// <summary>
    /// Creates and opens a connection — use when running multiple statements under
    /// one connection or a transaction (unit-of-work).
    /// </summary>
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a connection and begins a transaction, returning a <see cref="DbTransactionScope"/>
    /// unit-of-work. Pass its <see cref="DbTransactionScope.Transaction"/> to repository calls so
    /// multiple writes commit atomically.
    /// </summary>
    Task<DbTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
