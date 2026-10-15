using Echo.Core.Database;
using Echo.Core.Protocol;

namespace Echo.Core.Writes;

/// <summary>Directory snapshot upsert; entries are pre-sanitized JSON from the scanner.</summary>
internal static class ScanSnapshotWriter
{
    public static void Write(TransactionSql sql, ScanSnapshotItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FolderId) || string.IsNullOrWhiteSpace(item.Path) || string.IsNullOrWhiteSpace(item.UpdatedAt)
            || !double.IsFinite(item.MtimeMs) || string.IsNullOrEmpty(item.EntriesJson) || item.EntriesJson.Length > 4 * 1024 * 1024)
            throw new ArgumentException("Invalid directory snapshot.");
        sql.Run("""
            INSERT INTO scan_directory_snapshots (folder_id, path, mtime_ms, entries_json, updated_at) VALUES ($0, $1, $2, $3, $4)
            ON CONFLICT(folder_id, path) DO UPDATE SET mtime_ms = excluded.mtime_ms, entries_json = excluded.entries_json, updated_at = excluded.updated_at
            """, item.FolderId, item.Path, Math.Round(item.MtimeMs), item.EntriesJson, item.UpdatedAt);
    }
}
