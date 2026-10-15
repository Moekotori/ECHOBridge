using Echo.Core.Database;
using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Writes;

/// <summary>Owns bounded execution sweeps. Media adapters acknowledge only after their producers drain.</summary>
internal static class ScanExecutionDispatcher
{
    public const string Capability = "library.scanDispatch/v1";

    public static bool IsAvailable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='core_scan_dispatch'";
        return command.ExecuteScalar() is not null;
    }

    public static void Cleanup(TransactionSql sql, string job)
    {
        // Older stdio clients can still use checkpoint capabilities on their existing schema.
        if (sql.Value("SELECT 1 FROM sqlite_master WHERE type='table' AND name='core_scan_dispatch'") is not null)
            sql.Run("DELETE FROM core_scan_dispatch WHERE job_id=$0", job);
    }

    public static ScanCheckpointResult Execute(TransactionSql sql, string job, ScanCheckpoint request)
    {
        if (!Guid.TryParseExact(request.Owner, "D", out _) || request.Limit is < 1 or > 64
            || request.Cursor < 0
            || sql.Value("SELECT 1 FROM core_scan_tasks WHERE job_id=$0 AND sealed=1", job) is null)
            throw new ArgumentException("Invalid scan dispatch owner or manifest.");

        if (request.Action == "dispatchStart")
        {
            // A replacement executor rebuilds only unfinished work. Committed stages never reset.
            sql.Run("""
                INSERT INTO core_scan_dispatch(job_id,owner) VALUES($0,$1)
                ON CONFLICT(job_id) DO UPDATE SET owner=$1,cursor=0,pass=0,page_end=0,exhausted=0
                """, job, request.Owner);
            return new(Remaining: ScanExecutionCheckpoint.Remaining(sql, job));
        }

        long cursor, end, pass;
        bool exhausted;
        using (var command = sql.Command("SELECT cursor,page_end,pass,exhausted FROM core_scan_dispatch WHERE job_id=$0 AND owner=$1",
            job, request.Owner))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("Scan dispatch executor was replaced.");
            cursor = reader.GetInt64(0); end = reader.GetInt64(1); pass = reader.GetInt64(2);
            exhausted = reader.GetInt64(3) != 0;
        }

        if (request.Action == "dispatchPage")
        {
            // Media descriptors stay out of the durable mutation journal. Its 256 KiB
            // recovery budget must also hold the job state and escaped JSON envelope.
            var selected = new List<ScanCheckpointFile>();
            if (!exhausted && end > cursor)
                ScanExecutionCheckpoint.ReadFiles(sql, job, null, selected, request.Limit, cursor, end);
            return new(Files: selected, Remaining: ScanExecutionCheckpoint.Remaining(sql, job), Cursor: end, Exhausted: exhausted);
        }

        if (request.Stage == "complete")
        {
            if (request.Cursor != end || end <= cursor || sql.Value("""
                SELECT 1 FROM core_scan_files WHERE job_id=$0 AND stage IN ('pending','reading')
                  AND rowid>$1 AND rowid<=$2 LIMIT 1
                """, job, cursor, end) is not null)
                throw new InvalidOperationException("Scan dispatch page has unfinished files.");
            cursor = end;
        }
        else if (request.Stage == "deferred")
        {
            if (request.Cursor <= cursor || request.Cursor > end || sql.Value("""
                SELECT 1 FROM core_scan_files WHERE job_id=$0 AND rowid=$1 AND stage IN ('pending','reading')
                """, job, request.Cursor) is null)
                throw new ArgumentException("Invalid deferred scan ordinal.");
            // Preserve the refused file; following files can execute during this sweep.
            cursor = request.Cursor;
        }
        else if (request.Stage != "" || request.Cursor != 0)
            throw new ArgumentException("Invalid scan dispatch acknowledgement.");

        var remaining = ScanExecutionCheckpoint.Remaining(sql, job);
        var files = new List<ScanCheckpointFile>();
        if (!exhausted && remaining > 0)
        {
            ScanExecutionCheckpoint.ReadFiles(sql, job, null, files, request.Limit, cursor);
            if (files.Count == 0 && cursor > 0)
            {
                if (pass >= 2) exhausted = true;
                else
                {
                    cursor = 0; pass++;
                    ScanExecutionCheckpoint.ReadFiles(sql, job, null, files, request.Limit);
                }
            }
            if (files.Count == 0 && !exhausted)
                throw new InvalidOperationException("Scan dispatch manifest count is inconsistent.");
        }
        end = files.Count > 0 ? files[^1].Ordinal : cursor;
        sql.Run("UPDATE core_scan_dispatch SET cursor=$2,pass=$3,page_end=$4,exhausted=$5 WHERE job_id=$0 AND owner=$1",
            job, request.Owner, cursor, pass, end, exhausted ? 1 : 0);
        return new(Remaining: remaining, Cursor: end, Exhausted: exhausted);
    }
}
