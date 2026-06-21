using System.Data;
using Npgsql;

namespace EduTech.Shared.Persistence;

/// <summary>
/// A unit-of-work over a single connection + transaction (mirrors CardService's DbTransactionScope,
/// adapted to Npgsql). Begin one at the use-case level, pass <see cref="Transaction"/> to each repo
/// call, then commit. If disposed without committing, the transaction is rolled back.
///
/// Usage:
///   await using DbTransactionScope tx = await _connectionFactory.BeginTransactionAsync(ct);
///   await _schoolRepo.CreateAsync(school, tx.Transaction, ct);
///   await _ownerRepo.CreateAsync(owner, tx.Transaction, ct);
///   await tx.CommitAsync(ct);
/// </summary>
public sealed class DbTransactionScope : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private bool _completed;

    internal DbTransactionScope(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public IDbTransaction Transaction => _transaction;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.CommitAsync(cancellationToken);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.RollbackAsync(cancellationToken);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            // Best-effort rollback for the not-committed path (exception, early return).
            try
            {
                await _transaction.RollbackAsync();
            }
            catch
            {
                // Connection may already be broken; nothing useful to do here.
            }
        }

        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
