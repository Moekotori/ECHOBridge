using Echo.Core.Database;
using Echo.Core.Protocol;

namespace Echo.Core.Writes;

/// <summary>Invisible inbox rows recorded with the same transaction as each added track.</summary>
internal static class ScanInboxStaging
{
    public static void RecordAdded(TransactionSql sql, string scanJobId, string? timestamp, List<ScanWriteResult> tracks)
    {
        if (tracks.TrueForAll(static track => !track.Added)) return;
        if (!Guid.TryParse(scanJobId, out _) || string.IsNullOrWhiteSpace(timestamp) || timestamp.Length > 64)
            throw new ArgumentException("Invalid scan inbox identity or timestamp.");

        // A failed scan never exposed its zero-count placeholder. Reclaim it on the next Core write.
        sql.Run("""
            DELETE FROM library_inbox_batches
            WHERE added_count = 0
              AND scan_job_id IN (SELECT id FROM scan_jobs WHERE status IN ('failed', 'cancelled', 'completed'))
            """);
        sql.Run("""
            INSERT OR IGNORE INTO library_inbox_batches
              (id, scan_job_id, folder_id, folder_name, folder_path,
               added_count, missing_cover_count, metadata_issue_count, created_at, finished_at)
            SELECT $0, $0, folders.id, folders.name, folders.path, 0, 0, 0, $1, $1
            FROM scan_jobs INNER JOIN folders ON folders.id = scan_jobs.folder_id
            WHERE scan_jobs.id = $0 AND scan_jobs.status IN ('queued', 'running')
            """, scanJobId, timestamp);
        if (sql.Value("SELECT 1 FROM library_inbox_batches WHERE scan_job_id = $0", scanJobId) is null)
            throw new InvalidOperationException("Scan job is unavailable for Core inbox staging.");

        var position = Convert.ToInt64(sql.Value(
            "SELECT COALESCE(MAX(position) + 1, 0) FROM library_inbox_items WHERE batch_id = $0", scanJobId) ?? 0L);
        foreach (var track in tracks)
        {
            if (!track.Added) continue;
            if (sql.Run("""
                INSERT OR IGNORE INTO library_inbox_items (batch_id, track_id, position, created_at)
                VALUES ($0, $1, $2, $3)
                """, scanJobId, track.Id, position, timestamp) > 0) position++;
        }
    }
}
