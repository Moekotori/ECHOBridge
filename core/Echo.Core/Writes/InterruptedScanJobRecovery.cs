using Echo.Core.Database;
using Echo.Core.Maintenance;
using SQLitePCL;

namespace Echo.Core.Writes;

/// <summary>First desktop startup only. Bounded SQL batches preserve committed scan results and diagnostics.</summary>
internal static class InterruptedScanJobRecovery
{
    internal const string Message = "Scan was interrupted before it finished (the application exited while it was running)";
    private const int MaxErrorsJsonChars = 200 * (512 * 6 + 3) + 2;

    public static int Recover(string path, CancellationToken cancellation = default, DateTimeOffset? createdBefore = null)
    {
        using var connection = SortKeyMaintenanceSql.OpenWriter(path);
        using var interrupt = cancellation.Register(() => raw.sqlite3_interrupt(connection.Handle));
        SortKeyMaintenanceSql.Execute(connection, null, "CREATE TEMP TABLE interrupted_scan_batch(id INTEGER PRIMARY KEY)");
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var recovered = 0;
        var cursor = long.MinValue;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            using var transaction = connection.BeginTransaction();
            using var sql = new TransactionSql(connection, transaction);
            sql.Run("DELETE FROM interrupted_scan_batch");
            // A second-instance/open-file request can create a current-process job before full startup settles.
            sql.Run("""
                INSERT INTO interrupted_scan_batch SELECT rowid FROM scan_jobs WHERE rowid>$0 AND status IN ('queued','running')
                  AND ($1 IS NULL OR julianday(created_at) IS NULL OR julianday(created_at)<=julianday($1))
                ORDER BY rowid LIMIT 64
                """, cursor, createdBefore?.ToString("o"));
            if (sql.Value("SELECT MAX(id) FROM interrupted_scan_batch") is not long next) return recovered;
            sql.Run("""
                UPDATE scan_jobs SET status='cancelled',phase='cancelled',finished_at=COALESCE(finished_at,$0),updated_at=$0
                WHERE rowid IN (SELECT id FROM interrupted_scan_batch) AND cancel_requested=1
                """, timestamp);
            sql.Run("""
                DELETE FROM core_scan_files WHERE job_id IN (SELECT job_id FROM core_scan_tasks WHERE sealed=0)
                AND job_id IN (SELECT id FROM scan_jobs WHERE rowid IN (SELECT id FROM interrupted_scan_batch))
                """);
            sql.Run("""
                UPDATE core_scan_tasks SET file_count=0,processed_count=0,skipped_count=0 WHERE sealed=0
                AND job_id IN (SELECT id FROM scan_jobs WHERE rowid IN (SELECT id FROM interrupted_scan_batch))
                """);
            sql.Run("""
                DELETE FROM core_scan_seen_paths WHERE scan_job_id IN (SELECT job_id FROM core_scan_tasks WHERE sealed=0)
                AND scan_job_id IN (SELECT id FROM scan_jobs WHERE rowid IN (SELECT id FROM interrupted_scan_batch))
                """);
            sql.Run("""
                UPDATE scan_jobs SET status='queued',phase='queued',finished_at=NULL,updated_at=$0
                WHERE rowid IN (SELECT id FROM interrupted_scan_batch) AND cancel_requested=0
                  AND EXISTS(SELECT 1 FROM core_scan_tasks WHERE job_id=scan_jobs.id)
                """, timestamp);
            // SQLite retains at most 64 rowids; no scan rows or error strings cross into CLR memory.
            var count = sql.Run($"""
                UPDATE scan_jobs SET status='failed', phase='failed',
                  error_count=COALESCE(error_count,0)+1,
                  errors_json=CASE
                    WHEN length(errors_json)>{MaxErrorsJsonChars} THEN json_array('[unsafe scan error payload removed]',$0)
                    WHEN NOT json_valid(errors_json) THEN json_array($0)
                    WHEN json_type(errors_json)!='array' THEN json_array($0)
                    WHEN json_array_length(errors_json)>=200 THEN errors_json
                    ELSE json_insert(errors_json,'$[#]',$0) END,
                  finished_at=COALESCE(finished_at,$1), updated_at=$1
                WHERE rowid IN (SELECT id FROM interrupted_scan_batch) AND status IN ('queued','running')
                  AND NOT EXISTS(SELECT 1 FROM core_scan_tasks WHERE job_id=scan_jobs.id)
                """, Message, timestamp);
            sql.Run("DELETE FROM core_scan_files WHERE job_id IN (SELECT id FROM scan_jobs WHERE status IN ('cancelled','failed','completed'))");
            sql.Run("DELETE FROM core_scan_tasks WHERE job_id IN (SELECT id FROM scan_jobs WHERE status IN ('cancelled','failed','completed'))");
            cancellation.ThrowIfCancellationRequested();
            transaction.Commit();
            cursor = next;
            recovered += count;
        }
    }
}
