using System.Data;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Echo.Core.Database;

public sealed class ReadOnlyLibraryDatabase : IDisposable
{
    private readonly object _gate = new();
    private SqliteConnection? _connection;
    private BoundedStatementCache? _statements;
    private string? _openedPath;
    private long _openedGeneration = -1;
    private bool _disposed;
    private readonly int _budgetParts;

    public ReadOnlyLibraryDatabase(int budgetParts = 4) => _budgetParts = Math.Clamp(budgetParts, 1, 4);

    public SqliteConnection RequireConnection()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is null)
            {
                throw new InvalidOperationException("Core has not opened a library database.");
            }

            return _connection;
        }
    }

    public BoundedStatementCache Statements
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _statements ?? throw new InvalidOperationException("Core has not opened a library database.");
            }
        }
    }

    public long OpenedGeneration
    {
        get
        {
            lock (_gate)
            {
                return _openedGeneration;
            }
        }
    }

    public SchemaCapabilityReport Open(string databasePath, long generation)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new InvalidOperationException("A concrete library database path is required.");
        }

        if (databasePath is ":memory:" or "file::memory:")
        {
            throw new InvalidOperationException("Core refuses in-memory databases; use the authorized on-disk library.");
        }

        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("Authorized library database does not exist.", databasePath);
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is not null
                && string.Equals(_openedPath, databasePath, StringComparison.Ordinal)
                && _openedGeneration == generation)
            {
                return SchemaCapabilityProbe.Probe(_connection);
            }

            CloseUnlocked();
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                // Each lane owns its cache budget; shared-cache PRAGMAs would let
                // the aggregate connection shrink the interactive connection's cache.
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            };
            var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            ApplyLightweightPragmas(connection);
            RegisterSqlFunctions(connection);
            var report = SchemaCapabilityProbe.Probe(connection);
            if (!report.Ok)
            {
                connection.Dispose();
                throw new InvalidOperationException(report.Error ?? "Library schema is not compatible with Core.");
            }

            _connection = connection;
            _statements = new BoundedStatementCache(connection, ProtocolConstants.StatementCacheLimit * _budgetParts / 4);
            _openedPath = databasePath;
            _openedGeneration = generation;
            return report;
        }
    }

    public IDisposable BeginDeferredRead()
    {
        var connection = RequireConnection();
        using (var begin = connection.CreateCommand())
        {
            begin.CommandText = "BEGIN DEFERRED";
            begin.ExecuteNonQuery();
        }

        return new DeferredRead(connection);
    }

    public void InterruptReads()
    {
        // sqlite3_interrupt is the one SQLite call that is safe from another thread.
        // It only sets a flag; the DB thread stays the sole user of the connection.
        SqliteConnection? connection;
        lock (_gate)
        {
            connection = _connection;
        }

        if (connection is null || connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            raw.sqlite3_interrupt(connection.Handle);
        }
        catch (InvalidOperationException)
        {
            // Closed between the state check and the native call.
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            CloseUnlocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CloseUnlocked();
        }
    }

    private void CloseUnlocked()
    {
        DefaultCoverLookup.Invalidate(this);
        _statements?.Dispose();
        _statements = null;
        _connection?.Dispose();
        _connection = null;
        _openedPath = null;
        _openedGeneration = -1;
    }

    private static void RegisterSqlFunctions(SqliteConnection connection)
    {
        connection.CreateFunction("echo_genre_key", static (string? value) => GenreKey.FromTag(value), isDeterministic: true);
        connection.CreateFunction(
            "echo_artist_matches",
            static (string? value, string? key, string? strategy) => ArtistCredit.Matches(value, key, strategy) ? 1 : 0,
            isDeterministic: true);
    }

    private void ApplyLightweightPragmas(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            PRAGMA busy_timeout = 5000;
            PRAGMA temp_store = MEMORY;
            PRAGMA cache_size = {ProtocolConstants.SqliteCacheSizePages * _budgetParts / 4};
            PRAGMA mmap_size = {ProtocolConstants.SqliteMmapBytes * _budgetParts / 4};
            """;
            command.ExecuteNonQuery();
    }

    private sealed class DeferredRead : IDisposable
    {
        private readonly SqliteConnection _connection;
        private bool _disposed;

        public DeferredRead(SqliteConnection connection)
        {
            _connection = connection;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                using var commit = _connection.CreateCommand();
                commit.CommandText = "COMMIT";
                commit.ExecuteNonQuery();
            }
            catch
            {
                using var rollback = _connection.CreateCommand();
                rollback.CommandText = "ROLLBACK";
                rollback.ExecuteNonQuery();
            }
        }
    }
}
