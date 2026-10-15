using Microsoft.Data.Sqlite;

namespace Echo.Core.Maintenance;

// One bounded connection across slices; transactions never escape a single RPC.
public sealed class SortKeyWriter : IDisposable
{
    private SqliteConnection? _connection;
    private string? _path;

    public SqliteConnection Acquire(string path)
    {
        if (_connection is not null && _path == path) return _connection;
        Dispose();
        _connection = SortKeyMaintenanceSql.OpenWriter(path);
        _path = path;
        return _connection;
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        _path = null;
    }
}
