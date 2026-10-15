using Echo.Core.Database;
using Echo.Core.Maintenance;
using Echo.Core.Protocol;
using Echo.Core.Query;

namespace Echo.Core.Writes;

public static class ScanFinalizeWriter
{
    public const string Capability = "library.scanFinalize/v1";

    public static ScanFinalizeResult Execute(string databasePath, ScanFinalizeCommand request)
    {
        Validate(request);
        using var connection = SortKeyMaintenanceSql.OpenWriter(databasePath);
        using var transaction = connection.BeginTransaction();
        var sql = new TransactionSql(connection, transaction);
        var journal = new CommandReceiptJournal<ScanFinalizeCommand, ScanFinalizeResult>(sql,
            CoreJsonContext.Default.ScanFinalizeCommand, CoreJsonContext.Default.ScanFinalizeResult,
            state => new ScanFinalizeResult(state), libraryMutation: true);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var previous = journal.Find(request, now);
        if (previous is not null) return previous;

        if (sql.Value("""
            SELECT 1 FROM scan_jobs WHERE id = $0 AND folder_id = $1
              AND status IN ('queued', 'running') AND cancel_requested = 0
            """, request.ScanJobId, request.FolderId) is null)
            throw new InvalidOperationException("Scan job is no longer active.");
        var addedCount = Convert.ToInt32(sql.Value(
            "SELECT COUNT(*) FROM library_inbox_items WHERE batch_id = $0", request.ScanJobId) ?? 0);
        if (ScanExecutionCheckpoint.Exists(sql, request.ScanJobId)
            && (ScanExecutionCheckpoint.Remaining(sql, request.ScanJobId) != 0
                || Convert.ToInt64(sql.Value("SELECT sealed FROM core_scan_tasks WHERE job_id=$0",request.ScanJobId))!=1))
            throw new InvalidOperationException("Scan files have not finished.");
        if (addedCount != request.ExpectedAddedCount) throw new InvalidOperationException("Core inbox additions do not match the scan result.");

        string? batchId = null;
        if (addedCount > 0)
        {
            batchId = request.ScanJobId;
            if (sql.Value("SELECT 1 FROM library_inbox_batches WHERE id = $0", batchId) is null)
                throw new InvalidOperationException("Core inbox batch is missing.");
            var counts = ReadIssueCounts(sql, batchId);
            sql.Run("""
                UPDATE library_inbox_batches SET folder_name = $0, folder_path = $1,
                  added_count = $2, missing_cover_count = $3, metadata_issue_count = $4,
                  created_at = $5, finished_at = $6 WHERE id = $7
                """, request.FolderName, request.FolderPath, addedCount, counts.MissingCover,
                counts.MetadataIssue, request.ScanTimestamp, request.FinishedAt, batchId);
        }
        else sql.Run("DELETE FROM library_inbox_batches WHERE id = $0 AND added_count = 0", request.ScanJobId);

        if (request.MarkMissing)
            sql.Run("UPDATE folders SET last_scan_at = $0, updated_at = $0 WHERE id = $1", request.ScanTimestamp, request.FolderId);
        if (sql.Run("""
            UPDATE scan_jobs SET status = 'completed', phase = 'finished', finished_at = $0, updated_at = $1
            WHERE id = $2 AND folder_id = $3 AND status IN ('queued', 'running') AND cancel_requested = 0
            """, request.FinishedAt, DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"), request.ScanJobId, request.FolderId) != 1)
            throw new InvalidOperationException("Scan job completion was not committed.");
        sql.Run("""
            DELETE FROM library_inbox_batches WHERE id IN (
              SELECT id FROM library_inbox_batches WHERE added_count > 0
              ORDER BY finished_at DESC, created_at DESC LIMIT -1 OFFSET 30
            )
            """);
        var result = new ScanFinalizeResult("committed", batchId);
        ScanExecutionCheckpoint.Cleanup(sql, request.ScanJobId);
        journal.Save(request, result, now);
        transaction.Commit();
        return result;
    }

    private static (int MissingCover, int MetadataIssue) ReadIssueCounts(TransactionSql sql, string batchId)
    {
        var missing = InboxSelectionBuilder.MissingCover;
        var metadata = InboxSelectionBuilder.MetadataIssue;
        using var command = sql.Command($"""
            SELECT SUM(CASE WHEN {missing} THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {metadata} THEN 1 ELSE 0 END)
            FROM library_inbox_items INNER JOIN tracks ON tracks.id = library_inbox_items.track_id
            WHERE library_inbox_items.batch_id = $0 AND tracks.missing = 0 AND tracks.library_hidden = 0
            """, batchId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.IsDBNull(0) ? 0 : reader.GetInt32(0), reader.IsDBNull(1) ? 0 : reader.GetInt32(1))
            : (0, 0);
    }

    private static void Validate(ScanFinalizeCommand request)
    {
        if (!Guid.TryParse(request.ScanJobId, out _) || string.IsNullOrWhiteSpace(request.FolderId)
            || request.FolderId.Length > 512 || request.ExpectedAddedCount < 0
            || request.FolderName.Length > 512 || request.FolderPath.Length > 4096
            || string.IsNullOrWhiteSpace(request.ScanTimestamp) || request.ScanTimestamp.Length > 64
            || string.IsNullOrWhiteSpace(request.FinishedAt) || request.FinishedAt.Length > 64)
            throw new ArgumentException("Invalid scan finalization request.");
    }
}
