using Microsoft.Extensions.Logging;
using Npgsql;

namespace EduTech.Shared.Persistence;

/// <summary>
/// DEV convenience: applies the plain-SQL migrations in <c>Database/*.sql</c> at startup, each exactly
/// once, tracked in a <c>schema_migrations</c> table. Gated by config <c>Database:AutoMigrate</c> — flip
/// it OFF before launch and run migrations manually (the CardService pattern).
///
/// Baseline: when auto-migrate is first switched on against an ALREADY-populated DB (one set up by
/// hand), files up to <c>Database:BaselineThrough</c> are marked applied WITHOUT running (they were run
/// manually); newer files then apply automatically. A fresh/empty DB just runs everything in order.
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(string connectionString, ILogger<DatabaseMigrator> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task ApplyPendingAsync(string migrationsDirectory, string? baselineThrough,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(migrationsDirectory))
        {
            _logger.LogWarning("Auto-migrate: migrations directory not found at {Dir}; skipping.", migrationsDirectory);
            return;
        }

        List<string> files = Directory.GetFiles(migrationsDirectory, "*.sql")
            .Select(p => Path.GetFileName(p)!)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        await using NpgsqlConnection connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection,
            "CREATE TABLE IF NOT EXISTS schema_migrations (filename TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
            cancellationToken);

        HashSet<string> applied = await GetAppliedAsync(connection, cancellationToken);

        // First time on a hand-built DB: baseline the already-applied files instead of re-running them.
        if (applied.Count == 0 && !string.IsNullOrWhiteSpace(baselineThrough)
            && await TableExistsAsync(connection, "schools", cancellationToken))
        {
            foreach (string file in files.Where(f => string.CompareOrdinal(f, baselineThrough) <= 0))
            {
                await RecordAsync(connection, file, cancellationToken);
                applied.Add(file);
                _logger.LogInformation("Auto-migrate: baselined {File} (assumed already applied).", file);
            }
        }

        List<string> pending = files.Where(f => !applied.Contains(f)).ToList();
        if (pending.Count == 0)
        {
            _logger.LogInformation("Auto-migrate: schema up to date ({Count} applied).", applied.Count);
            return;
        }

        foreach (string file in pending)
        {
            string sql = await File.ReadAllTextAsync(Path.Combine(migrationsDirectory, file), cancellationToken);

            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using (NpgsqlCommand run = new NpgsqlCommand(sql, connection, transaction))
                {
                    await run.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (NpgsqlCommand record = new NpgsqlCommand(
                    "INSERT INTO schema_migrations (filename) VALUES (@f)", connection, transaction))
                {
                    record.Parameters.AddWithValue("f", file);
                    await record.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation("Auto-migrate: applied {File}.", file);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Auto-migrate: FAILED applying {File}; aborting startup.", file);
                throw;
            }
        }
    }

    /// <summary>Walks up from the content root to find the <c>Database</c> folder that holds the *.sql files.</summary>
    public static string? ResolveMigrationsDirectory(string contentRoot)
    {
        DirectoryInfo? dir = new DirectoryInfo(contentRoot);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Database");
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.sql").Length > 0)
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> GetAppliedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
        await using NpgsqlCommand cmd = new NpgsqlCommand("SELECT filename FROM schema_migrations", connection);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            set.Add(reader.GetString(0));
        }

        return set;
    }

    private static async Task RecordAsync(NpgsqlConnection connection, string file, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "INSERT INTO schema_migrations (filename) VALUES (@f) ON CONFLICT DO NOTHING", connection);
        cmd.Parameters.AddWithValue("f", file);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = new NpgsqlCommand("SELECT to_regclass(@t) IS NOT NULL", connection);
        cmd.Parameters.AddWithValue("t", table);
        object? result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
    }
}
