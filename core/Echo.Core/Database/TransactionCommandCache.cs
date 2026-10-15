using Microsoft.Data.Sqlite;

namespace Echo.Core.Database;

/// <summary>Only scalar/nonquery commands are reused. Reader-owning callers retain independent commands.</summary>
internal sealed class TransactionCommandCache(
    Func<string, object?[], SqliteCommand> create, int maxCommands = 32, int maxSqlChars = 131072) : IDisposable
{
    private readonly Dictionary<(string Sql, int Parameters), SqliteCommand> _commands = [];
    private int _sqlChars;
    private bool _disposed;

    public T Execute<T>(string sql, object?[] values, Func<SqliteCommand, T> execute)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = (sql, values.Length);
        if (!_commands.TryGetValue(key, out var command))
        {
            if (_commands.Count >= maxCommands || sql.Length > maxSqlChars - _sqlChars)
            {
                using var transient = create(sql, values);
                return execute(transient);
            }
            command = create(sql, values);
            _commands.Add(key, command);
            _sqlChars += sql.Length;
        }
        // Rebind every value, including NULL and changes of SQLite storage type.
        for (var index = 0; index < values.Length; index++) command.Parameters[index].Value = values[index] ?? DBNull.Value;
        try { return execute(command); }
        finally
        {
            // Do not hold the previous track's tag/JSON values until the next execution.
            foreach (SqliteParameter parameter in command.Parameters) parameter.Value = DBNull.Value;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var command in _commands.Values) command.Dispose();
        _commands.Clear();
        _sqlChars = 0;
    }
}
