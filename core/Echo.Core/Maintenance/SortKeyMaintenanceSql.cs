using Microsoft.Data.Sqlite;
using Echo.Core.Protocol;

namespace Echo.Core.Maintenance;

internal static class SortKeyMaintenanceSql
{
    public static SqliteConnection OpenWriter(string path)
    {
        // Match the Electron writer's busy_timeout so a concurrent main-process transaction
        // waits instead of failing the Core write immediately.
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
            Cache = SqliteCacheMode.Private, DefaultTimeout = ProtocolConstants.WriterBusyTimeoutMs / 1000,
        }.ConnectionString);
        try
        {
            connection.Open();
            Execute(connection, null, $"PRAGMA busy_timeout={ProtocolConstants.WriterBusyTimeoutMs}; PRAGMA cache_size=-1024; PRAGMA mmap_size=0; PRAGMA foreign_keys=ON;");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public static string Revision(SqliteConnection connection, SqliteTransaction? tx, SortKeySchema schema) =>
        $"{ProtocolConstants.SortKeyFormat}|rev:{Scalar(connection, tx, $"SELECT revision FROM {schema.RevisionTable} WHERE id=1") ?? 0}";

    public static object? Scalar(SqliteConnection connection, SqliteTransaction? tx, string sql, params object?[] values)
    {
        using var command = Command(connection, tx, sql, values);
        return command.ExecuteScalar();
    }
    public static void Execute(SqliteConnection connection, SqliteTransaction? tx, string sql, params object?[] values)
    {
        using var command = Command(connection, tx, sql, values);
        command.ExecuteNonQuery();
    }
    public static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? tx, string sql, params object?[] values)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue($"$p{i}", values[i] ?? DBNull.Value);
        return command;
    }
    public static void Flag(SqliteConnection connection, SqliteTransaction tx, string key, string value) =>
        Execute(connection, tx, "INSERT INTO library_maintenance_flags(key,value,updated_at) VALUES($p0,$p1,$p2) " +
            "ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at",
            key, value, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
}
