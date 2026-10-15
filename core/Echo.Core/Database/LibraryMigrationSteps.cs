using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Database;

internal static class LibraryMigrationSteps
{
    public static void Apply(SqliteConnection connection, SqliteTransaction transaction, JsonElement steps,
        CancellationToken cancellationToken = default)
    {
        foreach (var step in steps.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (step.TryGetProperty("sql", out var sql))
                Execute(connection, transaction, sql.GetString()!);
            else if (step.TryGetProperty("addColumn", out var column))
            {
                var table = column[0].GetString()!;
                var name = column[1].GetString()!;
                if (!HasColumn(connection, transaction, table, name))
                    Execute(connection, transaction, $"ALTER TABLE {Quote(table)} ADD COLUMN {column[2].GetString()}");
            }
            else if (step.TryGetProperty("if", out var condition))
            {
                if (Matches(connection, transaction, condition)) Apply(connection, transaction, step.GetProperty("then"), cancellationToken);
                else if (step.TryGetProperty("else", out var alternative)) Apply(connection, transaction, alternative, cancellationToken);
            }
            else if (step.TryGetProperty("optional", out var optional))
            {
                // Preserve the existing migrations' compatibility catch for incomplete historical schemas.
                try { Apply(connection, transaction, optional, cancellationToken); }
                catch (SqliteException) when (!cancellationToken.IsCancellationRequested) { }
            }
            else throw new InvalidOperationException("Unsupported library migration step.");
        }
    }

    private static bool Matches(SqliteConnection connection, SqliteTransaction transaction, JsonElement condition)
    {
        if (condition.TryGetProperty("not", out var inner)) return !Matches(connection, transaction, inner);
        if (condition.TryGetProperty("column", out var column))
            return HasColumn(connection, transaction, column[0].GetString()!, column[1].GetString()!);
        if (condition.TryGetProperty("existsSql", out var sql))
        {
            using var command = Command(connection, transaction, sql.GetString()!);
            return command.ExecuteScalar() is { } value && value != DBNull.Value;
        }
        throw new InvalidOperationException("Unsupported library migration condition.");
    }

    private static bool HasColumn(SqliteConnection connection, SqliteTransaction transaction, string table, string column)
    {
        using var command = Command(connection, transaction, $"PRAGMA table_info({Quote(table)})");
        using var rows = command.ExecuteReader();
        while (rows.Read()) if (string.Equals(rows.GetString(1), column, StringComparison.Ordinal)) return true;
        return false;
    }

    internal static int Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        return command.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
