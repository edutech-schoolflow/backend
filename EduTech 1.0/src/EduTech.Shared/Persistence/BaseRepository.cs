using System.Data;
using Dapper;

namespace EduTech.Shared.Persistence;

/// <summary>
/// Base for ALL repositories. Wraps Dapper with connection management and cancellation.
///
/// Use this directly only for:
///   • global (non-tenant) tables — staff_users, parents, child_profiles, platform_admins, schools;
///   • parent-facing reads, which cross schools and are authorized by ownership
///     (parent → parent_children → students → invoices), NOT by tenant match.
///
/// For per-school tenant data, derive from <see cref="TenantRepository"/> instead so every
/// query is bound to the current school via @SchoolId.
///
/// Every helper accepts an optional <see cref="IDbTransaction"/>: pass null (the default) for a
/// standalone connection-per-call, or pass a <see cref="DbTransactionScope.Transaction"/> to enlist
/// the call in a unit-of-work so multiple writes commit atomically (CardService pattern).
/// </summary>
public abstract class BaseRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    protected BaseRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    protected async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null,
        CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        if (transaction is not null)
        {
            IEnumerable<T> txRows = await transaction.Connection!.QueryAsync<T>(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
            return txRows.ToList();
        }

        using IDbConnection connection = _connectionFactory.CreateConnection();
        IEnumerable<T> rows = await connection.QueryAsync<T>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    protected async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null,
        CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        if (transaction is not null)
        {
            return await transaction.Connection!.QuerySingleOrDefaultAsync<T>(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        }

        using IDbConnection connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    protected async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null,
        CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        if (transaction is not null)
        {
            return await transaction.Connection!.QueryFirstOrDefaultAsync<T>(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        }

        using IDbConnection connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<T>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    protected async Task<int> ExecuteAsync(string sql, object? parameters = null,
        CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        if (transaction is not null)
        {
            return await transaction.Connection!.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        }

        using IDbConnection connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    protected async Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null,
        CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        if (transaction is not null)
        {
            return await transaction.Connection!.ExecuteScalarAsync<T>(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        }

        using IDbConnection connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<T>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }
}
