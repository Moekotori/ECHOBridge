using Echo.Core.Database;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query.Sql;

internal static class CachedQuery
{
    public static object? Scalar(ReadOnlyLibraryDatabase database, string sql, Action<SqliteCommand>? bind = null)
    {
        var command = database.Statements.Get(sql);
        bind?.Invoke(command);
        command.UseNamedPlaceholders();
        return command.ExecuteScalar();
    }

    public static SqliteDataReader Reader(ReadOnlyLibraryDatabase database, string sql, Action<SqliteCommand>? bind = null)
    {
        var command = database.Statements.Get(sql);
        bind?.Invoke(command);
        command.UseNamedPlaceholders();
        return command.ExecuteReader();
    }
}
