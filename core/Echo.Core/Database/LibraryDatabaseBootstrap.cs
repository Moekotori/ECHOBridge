using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Echo.Core.Database;

public sealed record LibraryBootstrapResult(int AppliedCount, int SchemaVersion, string SchemaDigest);

/// <summary>Creates/upgrades the configured library before any service session can open it.</summary>
public static class LibraryDatabaseBootstrap
{
    public static LibraryBootstrapResult Initialize(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var catalog = LibraryMigrationCatalog.Load();
        try { return Initialize(path, catalog.RootElement, cancellationToken); }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
    }

    internal static LibraryBootstrapResult Initialize(string path, JsonElement catalog, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("Library bootstrap requires an absolute path.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5,
        }.ToString());
        connection.Open();
        using var interruption = cancellationToken.Register(() => raw.sqlite3_interrupt(connection.Handle));
        cancellationToken.ThrowIfCancellationRequested();
        var migrations = catalog.GetProperty("migrations");
        var latest = migrations.EnumerateArray().Max(item => item.GetProperty("id").GetInt32());
        var applied = ReadApplied(connection);
        if (applied.Any(id => id > latest))
            throw new InvalidOperationException("Library was migrated by a newer service; refusing to downgrade.");
        var pending = migrations.EnumerateArray().Any(item => !applied.Contains(item.GetProperty("id").GetInt32()));
        if (!pending)
        {
            if (!SchemaCapabilityProbe.Probe(connection).Ok)
                throw new InvalidOperationException("Managed library schema is not ready.");
            return new(0, latest, catalog.GetProperty("digest").GetString()!);
        }
        // Full checks protect schema writes; an unchanged schema avoids scanning the entire library at every start.
        AssertHealthy(connection);
        // Match existing durable behavior; the bootstrap connection is small and immediately disposed.
        LibraryMigrationSteps.Execute(connection, null,
            "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; "
            + "PRAGMA busy_timeout=5000; PRAGMA temp_store=FILE; PRAGMA cache_size=-8192; PRAGMA mmap_size=0;");
        LibraryMigrationSteps.Execute(connection, null, catalog.GetProperty("migrationTableSql").GetString()!);
        var count = 0;
        foreach (var migration in migrations.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = migration.GetProperty("id").GetInt32();
            if (applied.Contains(id)) continue;
            using var transaction = connection.BeginTransaction();
            LibraryMigrationSteps.Apply(connection, transaction, migration.GetProperty("steps"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using var receipt = connection.CreateCommand();
            receipt.Transaction = transaction;
            receipt.CommandText = "INSERT INTO schema_migrations(id,applied_at) VALUES($id,$at)";
            receipt.Parameters.AddWithValue("$id", id);
            receipt.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            receipt.ExecuteNonQuery();
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            count++;
        }
        if (count > 0) AssertHealthy(connection);
        if (!SchemaCapabilityProbe.Probe(connection).Ok)
            throw new InvalidOperationException("Managed library schema is not ready.");
        return new(count, latest, catalog.GetProperty("digest").GetString()!);
    }

    private static HashSet<int> ReadApplied(SqliteConnection connection)
    {
        using var table = connection.CreateCommand();
        table.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='schema_migrations'";
        if (table.ExecuteScalar() is null) return [];
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM schema_migrations LIMIT 257";
        using var rows = command.ExecuteReader();
        var ids = new HashSet<int>();
        while (rows.Read())
        {
            if (ids.Count >= 256) throw new InvalidOperationException("Library migration history exceeds its budget.");
            ids.Add(rows.GetInt32(0));
        }
        return ids;
    }

    private static void AssertHealthy(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check(1)";
        if (!string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
            throw new InvalidOperationException("Library health check failed; preserve the database for recovery.");
    }
}
