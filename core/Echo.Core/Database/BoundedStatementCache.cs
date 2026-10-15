using Microsoft.Data.Sqlite;

namespace Echo.Core.Database;

public sealed class BoundedStatementCache : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly int _limit;
    private readonly Dictionary<string, SqliteCommand> _commands = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = [];
    private bool _disposed;

    public BoundedStatementCache(SqliteConnection connection, int limit)
    {
        _connection = connection;
        _limit = Math.Max(1, limit);
    }

    public SqliteCommand Get(string sql)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_commands.TryGetValue(sql, out var existing))
        {
            _order.Remove(sql);
            _order.AddLast(sql);
            existing.Parameters.Clear();
            return existing;
        }

        while (_commands.Count >= _limit && _order.First is { } oldest)
        {
            _order.RemoveFirst();
            if (_commands.Remove(oldest.Value, out var evicted))
            {
                evicted.Dispose();
            }
        }

        var command = _connection.CreateCommand();
        command.CommandText = sql;
        _commands[sql] = command;
        _order.AddLast(sql);
        return command;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var command in _commands.Values)
        {
            command.Dispose();
        }

        _commands.Clear();
        _order.Clear();
    }
}
