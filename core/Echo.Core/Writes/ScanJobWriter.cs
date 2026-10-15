using System.Text.Json;
using Echo.Core.Database;
using Echo.Core.Maintenance;
using Echo.Core.Protocol;

namespace Echo.Core.Writes;

/// <summary>Durable task state shares the scan writer and its bounded sequence receipts.</summary>
public static class ScanJobWriter
{
    public const string Capability = "library.scanJobs/v1";
    private const int MaxActiveJobs = 64;
    private const int MaxErrorsJsonChars = 200 * (512 * 6 + 3) + 2;

    public static ScanJobCommandResult Execute(string path, ScanJobCommand request)
    {
        if (request.Operation == "checkpointRead")
        {
            if (!Guid.TryParseExact(request.JobId,"D",out _) || request.Checkpoint?.Action is not ("list" or "page" or "task" or "dispatchPage"))
                throw new ArgumentException("Invalid checkpoint read.");
            using var readConnection=SortKeyMaintenanceSql.OpenWriter(path);
            using var readTransaction=readConnection.BeginTransaction();
            using var readSql=new TransactionSql(readConnection,readTransaction);
            return new("committed",Checkpoint:ScanExecutionCheckpoint.Execute(readSql,request.JobId,request.Checkpoint,
                DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")));
        }
        Validate(request);
        using var connection = SortKeyMaintenanceSql.OpenWriter(path);
        using var transaction = connection.BeginTransaction();
        using var sql = new TransactionSql(connection, transaction);
        var journal = new MutationStreamJournal<ScanJobCommand, ScanJobCommandResult>(sql,
            CoreJsonContext.Default.ScanJobCommand, CoreJsonContext.Default.ScanJobCommandResult,
            state => new(state));
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var previous = journal.Find(request, now);
        if (previous is not null) return previous;
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(now).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        ScanCheckpointResult? checkpoint = null;
        if (request.Operation == "checkpoint")
            checkpoint = ScanExecutionCheckpoint.Execute(sql, request.JobId,
                request.Checkpoint ?? throw new ArgumentException("Checkpoint required."), timestamp);
        else if (request.Operation == "create")
        {
            if (sql.Value("SELECT 1 FROM folders WHERE id=$0 AND status='active'", request.FolderId) is null)
                throw new ArgumentException("Scan folder is not active.");
            if (Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM scan_jobs WHERE status IN ('queued','running')")) >= MaxActiveJobs)
                throw new InvalidOperationException("Scan task queue is full.");
            sql.Run("""
                INSERT INTO scan_jobs(id,folder_id,status,phase,errors_json,created_at,updated_at)
                VALUES($0,$1,'queued','queued','[]',$2,$2)
                """, request.JobId, request.FolderId, timestamp);
        }
        else
        {
            var current = Read(sql, request.JobId);
            var terminal = current.Status is "completed" or "cancelled" or "failed";
            if (request.Operation == "cancel")
            {
                if (!terminal)
                {
                    var finish = current.Status == "queued" || request.FinishCancellation;
                    sql.Run("""
                        UPDATE scan_jobs SET cancel_requested=1,status=$0,phase=$1,finished_at=$2,updated_at=$3 WHERE id=$4
                        """, finish ? "cancelled" : current.Status, finish ? "cancelled" : current.Phase,
                        finish ? timestamp : current.FinishedAt, timestamp, request.JobId);
                }
            }
            else if (!terminal || current.Status == "completed" && request.Patch?.Status == "failed")
            {
                var patch = request.Patch!;
                var cancelling = current.CancelRequested || patch.CancelRequested == true;
                var status = patch.Status ?? current.Status;
                if (status=="completed" && ScanExecutionCheckpoint.Exists(sql,request.JobId)
                    && ScanExecutionCheckpoint.Remaining(sql,request.JobId)>0)
                    throw new InvalidOperationException("Unfinished scan cannot be completed.");
                var phase = patch.Phase ?? current.Phase;
                var finishedAt = patch.FinishedAt ?? current.FinishedAt;
                if (cancelling && status == "completed") { status = "cancelled"; phase = "cancelled"; finishedAt ??= timestamp; }
                var total = patch.TotalFiles ?? current.TotalFiles;
                var processed = patch.ProcessedFiles ?? current.ProcessedFiles;
                var skipped = patch.SkippedFiles ?? current.SkippedFiles;
                if (ScanExecutionCheckpoint.Exists(sql, request.JobId)
                    && Convert.ToInt64(sql.Value("SELECT sealed FROM core_scan_tasks WHERE job_id=$0",request.JobId))==1)
                { total=current.TotalFiles; processed=current.ProcessedFiles; skipped=current.SkippedFiles; }
                var errors = BoundErrors(patch.Errors ?? current.Errors);
                var errorCount = patch.ErrorCount ?? (patch.Errors?.Count ?? current.ErrorCount);
                if (ScanExecutionCheckpoint.Exists(sql,request.JobId))
                {
                    errors=BoundErrors(errors.Concat(current.Errors).Distinct());
                    errorCount=Math.Max(errorCount,current.ErrorCount);
                }
                // Commit facts are written with the mutation, not by a later progress report.
                sql.Run("""
                    UPDATE scan_jobs SET status=$0,phase=$1,discovered_count=$2,total_files=$2,
                      parsed_count=$3,processed_files=$3,skipped_count=$4,skipped_files=$4,
                      added_tracks=$5,updated_tracks=$6,removed_tracks=$7,cover_count=$8,error_count=$9,
                      errors_json=$10,cancel_requested=$11,started_at=$12,finished_at=$13,updated_at=$14 WHERE id=$15
                    """, status, phase, total, processed, skipped, current.AddedTracks,
                    current.UpdatedTracks, current.RemovedTracks,
                    patch.CoverCount ?? current.CoverCount, errorCount,
                    JsonSerializer.Serialize(errors, CoreJsonContext.Default.ListString), cancelling ? 1 : 0,
                    patch.StartedAt ?? current.StartedAt, finishedAt, timestamp, request.JobId);
            }
        }
        var state = request.Checkpoint?.Action == "list" ? null : Read(sql, request.JobId);
        if (state?.Status is "completed" or "cancelled" or "failed") ScanExecutionCheckpoint.Cleanup(sql, request.JobId);
        var result = new ScanJobCommandResult("committed", state, Checkpoint: checkpoint);
        journal.Save(request, result, now);
        transaction.Commit();
        return result;
    }

    private static ScanJobState Read(TransactionSql sql, string id)
    {
        using var command = sql.Command("""
            SELECT id,folder_id,status,phase,COALESCE(discovered_count,total_files,0),COALESCE(parsed_count,processed_files,0),
              COALESCE(skipped_count,skipped_files,0),added_tracks,updated_tracks,removed_tracks,cover_count,error_count,
              CASE WHEN length(errors_json)<=$1 AND json_valid(errors_json) THEN errors_json ELSE '[]' END,
              started_at,finished_at,cancel_requested FROM scan_jobs WHERE id=$0
            """, id, MaxErrorsJsonChars);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new ArgumentException("Unknown scan job.");
        var errors = new List<string>();
        using (var document = JsonDocument.Parse(reader.GetString(12)))
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (errors.Count == 200) break;
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var message = element.GetString()!;
                        errors.Add(message.Length > 512 ? message[..512] : message);
                    }
                }
        long Count(int index) => reader.IsDBNull(index) ? 0 : reader.GetInt64(index);
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            Count(4), Count(5), Count(6), Count(7), Count(8), Count(9), Count(10), Count(11), BoundErrors(errors),
            reader.IsDBNull(13) ? null : reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14), Count(15) != 0);
    }

    private static List<string> BoundErrors(IEnumerable<string> messages)
    {
        var result = new List<string>();
        var remaining = 16_384;
        foreach (var message in messages)
        {
            if (remaining == 0 || result.Count == 200) break;
            var length = Math.Min(message.Length, Math.Min(512, remaining));
            result.Add(message[..length]);
            remaining -= length;
        }
        return result;
    }

    private static void Validate(ScanJobCommand request)
    {
        if (!Guid.TryParseExact(request.JobId, "D", out _) || request.Stream is null
            || request.Operation is not ("create" or "update" or "cancel" or "checkpoint"))
            throw new ArgumentException("Invalid scan task command.");
        if (request.Operation == "create" && (string.IsNullOrWhiteSpace(request.FolderId) || request.FolderId.Length > 512))
            throw new ArgumentException("Invalid scan folder.");
        if (request.Operation != "update") return;
        var patch = request.Patch ?? throw new ArgumentException("Scan progress required.");
        if (patch.Status is not (null or "queued" or "running" or "completed" or "failed" or "cancelled")
            || patch.Phase is not (null or "queued" or "discovering" or "checking_cache" or "reading_metadata"
                or "extracting_covers" or "grouping_albums" or "writing_database" or "finished" or "failed" or "cancelled")
            || new[] { patch.TotalFiles, patch.ProcessedFiles, patch.SkippedFiles, patch.AddedTracks, patch.UpdatedTracks,
                patch.RemovedTracks, patch.CoverCount, patch.ErrorCount }.Any(count => count is < 0 or > 9_007_199_254_740_991)
            || patch.Errors?.Count > 200 || patch.Errors?.Any(error => error is null || error.Length > 512) == true
            || patch.StartedAt?.Length > 64 || patch.FinishedAt?.Length > 64)
            throw new ArgumentException("Invalid or oversized scan progress.");
    }
}
