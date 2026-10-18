using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;
using static Echo.Core.Maintenance.SortKeyMaintenanceSql;

namespace Echo.Core.Maintenance;

// Owned by the interactive executor. One bounded writer, no whole-library snapshot.
public sealed class SortKeyMaintenance
{
    private sealed record Cursor(string Revision, bool Prune = true, string? AfterId = null, long? AfterRowId = null);
    private sealed record Pending(string Token, SortKeySchema Schema, Cursor Cursor, SortKeyBatch Batch);
    private readonly Dictionary<string, Cursor> _cursors = [];
    private Pending? _pending;
    private readonly SortKeyWriter _writer = new();

    public void Clear() { ReleasePending(); _cursors.Clear(); }
    public void ReleasePending() { _pending = null; _writer.Dispose(); }
    public SortKeyMaintenanceResult Discard(string? token)
    {
        if (_pending?.Token == token) ReleasePending();
        return new(false);
    }

    public SortKeyMaintenanceResult Prepare(SqliteConnection connection, string? kind)
    {
        try { return PrepareBatch(connection, kind); }
        catch { ReleasePending(); throw; }
    }

    private SortKeyMaintenanceResult PrepareBatch(SqliteConnection connection, string? kind)
    {
        var schema = SortKeySchema.For(kind);
        _pending = null;
        using var tx = connection.BeginTransaction(deferred: true);
        var revision = Revision(connection, tx, schema);
        if (Convert.ToString(Scalar(connection, tx, "SELECT value FROM library_maintenance_flags WHERE key=$p0", schema.Flag)) == revision)
        {
            _cursors.Remove(schema.Kind);
            _writer.Dispose();
            if (schema.Kind == "track" && Scalar(connection, tx,
                "SELECT value FROM library_maintenance_flags WHERE key=$p0", ProtocolConstants.TrackPresentCountFlagKey) is null)
            {
                tx.Commit();
                var repairToken = Guid.NewGuid().ToString("N");
                _pending = new(repairToken, schema, new(revision, Prune: false), new() { End = true });
                return new(false, repairToken, []);
            }
            return new(true);
        }
        if (!_cursors.TryGetValue(schema.Kind, out var cursor) || cursor.Revision != revision) cursor = new(revision);
        var batch = SortKeyBatchReader.Read(connection, tx, schema, cursor.Prune, cursor.AfterId, cursor.AfterRowId);
        tx.Commit();
        var token = Guid.NewGuid().ToString("N");
        _pending = new(token, schema, cursor, batch);
        return new(false, token, batch.Rows.Select(row => row.Texts.Select(text => text ?? "").ToArray()).ToArray());
    }

    public SortKeyMaintenanceResult Commit(string path, SortKeyMaintenanceParams? args)
    {
        try
        {
            var result = CommitBatch(path, args);
            if (result.Ready || _cursors.Count == 0) _writer.Dispose();
            return result;
        }
        catch
        {
            _writer.Dispose();
            throw;
        }
    }

    private SortKeyMaintenanceResult CommitBatch(string path, SortKeyMaintenanceParams? args)
    {
        var pending = _pending;
        if (pending is null || pending.Token != args?.Token || pending.Schema.Kind != args.Kind)
            throw new InvalidOperationException("Sort-key batch token is not current.");
        var keys = args.Keys;
        if (keys is null || keys.Length != pending.Batch.Rows.Count || keys.Any(row => row is null || row.Length != pending.Schema.KeyNames.Length
            || row.Any(key => key is null || key.Length > 262144)) || keys.Sum(row => row.Sum(key => (long)key.Length)) > 524288)
            throw new InvalidOperationException("Sort-key result dimensions or text budget are invalid.");
        var connection = _writer.Acquire(path);
        using var tx = connection.BeginTransaction(deferred: false);
        var schema = pending.Schema;
        if (Revision(connection, tx, schema) != pending.Cursor.Revision)
        {
            _pending = null;
            _cursors.Remove(schema.Kind);
            return new(false);
        }
        var batch = pending.Batch;
        foreach (var id in batch.DeletedIds)
            Execute(connection, tx, $"DELETE FROM {schema.Table} WHERE {schema.IdColumn}=$p0", id);
        WriteRows(connection, tx, schema, batch.Rows, keys);
        var ready = !pending.Cursor.Prune && batch.End;
        if (ready)
        {
            Flag(connection, tx, schema.Flag, pending.Cursor.Revision);
            if (schema.Kind == "track") Flag(connection, tx, ProtocolConstants.TrackPresentCountFlagKey,
                Convert.ToString(Scalar(connection, tx, $"SELECT COUNT(*) FROM {schema.Table}"))!);
        }
        // Advance in-memory state only after all writes and the ready marker commit.
        tx.Commit();
        _pending = null;
        if (ready) _cursors.Remove(schema.Kind);
        else _cursors[schema.Kind] = pending.Cursor with {
            Prune = pending.Cursor.Prune && !batch.End,
            AfterId = batch.LastId ?? pending.Cursor.AfterId,
            AfterRowId = batch.LastRowId ?? pending.Cursor.AfterRowId,
        };
        return new(ready, WroteRows: batch.Rows.Count);
    }

    private static void WriteRows(SqliteConnection connection, SqliteTransaction tx, SortKeySchema schema,
        List<SortKeyRow> rows, string[][] keys)
    {
        var fields = schema.KeyNames.SelectMany(name => new[] { $"{name}_key", $"{name}_text" }).ToArray();
        var columns = new[] { schema.IdColumn }.Concat(fields).Append(schema.UpdatedColumn).ToArray();
        var writeView = schema.Kind == "track" && Scalar(connection, tx,
            "SELECT 1 FROM sqlite_master WHERE type='view' AND name='local_track_sort_key_writes'") is not null;
        var target = writeView ? "local_track_sort_key_writes" : schema.Table;
        var sql = $"INSERT INTO {target} ({string.Join(',', columns)}) VALUES(" +
            string.Join(',', columns.Select((_, i) => $"$p{i}")) + ")";
        if (!writeView) sql += $" ON CONFLICT({schema.IdColumn}) DO UPDATE SET " +
            string.Join(',', columns.Skip(1).Select(column => $"{column}=excluded.{column}"));
        using var command = Command(connection, tx, sql, new object?[columns.Length]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            command.Parameters[0].Value = row.Id;
            for (var field = 0; field < row.Texts.Length; field++)
            {
                command.Parameters[1 + field * 2].Value = keys[index][field];
                command.Parameters[2 + field * 2].Value = (object?)row.Texts[field] ?? DBNull.Value;
            }
            command.Parameters[^1].Value = row.UpdatedAt;
            command.ExecuteNonQuery();
        }
    }
}
