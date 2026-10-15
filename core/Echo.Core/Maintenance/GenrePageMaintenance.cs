using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;
using static Echo.Core.Maintenance.SortKeyMaintenanceSql;

namespace Echo.Core.Maintenance;

// Disk-backed snapshot assembled in bounded slices; never a managed whole-library array.
public sealed class GenrePageMaintenance
{
    private readonly SortKeyWriter _writer = new();
    private string? _revision;
    private long? _after;
    private Pending? _pending;
    private sealed record Pending(string Token, List<GenreSourceRow> Rows, bool End);

    public void Clear() { _writer.Dispose(); _revision = null; _after = null; _pending = null; }
    public SortKeyMaintenanceResult Discard(string? token)
    {
        if (_pending?.Token == token) Clear();
        return new(false);
    }

    public SortKeyMaintenanceResult Prepare(SqliteConnection reader)
    {
        try
        {
            _pending = null;
            using var tx = reader.BeginTransaction(deferred: true);
            var revision = GenreMaintenanceStorage.Revision(reader, tx);
            if (Convert.ToString(Scalar(reader, tx, "SELECT value FROM library_maintenance_flags WHERE key=$p0", GenreMaintenanceStorage.FlagKey)) == revision)
            {
                Clear();
                return new(true);
            }
            if (_revision != revision) { Clear(); _revision = revision; }
            var (rows, end) = GenreMaintenanceStorage.Read(reader, tx, _after);
            tx.Commit();
            _pending = new(Guid.NewGuid().ToString("N"), rows, end);
            return new(false, _pending.Token, rows.Select(row => new[] { row.Genre ?? "" }).ToArray());
        }
        catch { Clear(); throw; }
    }

    public SortKeyMaintenanceResult Commit(string path, SortKeyMaintenanceParams? args)
    {
        try
        {
            var result = CommitBatch(path, args);
            if (result.Ready) Clear();
            return result;
        }
        catch { Clear(); throw; }
    }

    private SortKeyMaintenanceResult CommitBatch(string path, SortKeyMaintenanceParams? args)
    {
        var pending = _pending;
        if (pending is null || args?.Token != pending.Token || args.Kind != "genre") throw new InvalidOperationException("Genre batch token is not current.");
        var keys = args.Keys;
        if (keys is null || keys.Length != pending.Rows.Count || keys.Any(row => row is null || row.Length != 2
            || row.Any(key => key is null || key.Length > 262144)) || keys.Sum(row => row.Sum(key => (long)key.Length)) > 524288)
            throw new InvalidOperationException("Genre result dimensions or text budget are invalid.");
        var db = _writer.Acquire(path);
        if (_after is null) GenreMaintenanceStorage.Initialize(db);
        bool changed;
        using (var tx = db.BeginTransaction(deferred: false))
        {
            changed = GenreMaintenanceStorage.Revision(db, tx) != _revision;
            if (!changed)
            {
                GenreMaintenanceStorage.Append(db, tx, pending.Rows, keys);
                if (pending.End)
                {
                    GenreMaintenanceStorage.Publish(db, tx);
                    Flag(db, tx, GenreMaintenanceStorage.FlagKey, _revision!);
                }
                tx.Commit();
            }
        }
        if (changed) { Clear(); return new(false); }
        if (pending.Rows.Count > 0) _after = pending.Rows[^1].RowId;
        _pending = null;
        return new(pending.End, WroteRows: pending.Rows.Count);
    }
}
