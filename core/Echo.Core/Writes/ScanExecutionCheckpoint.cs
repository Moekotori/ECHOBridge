using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Echo.Core.Database;
using Echo.Core.Protocol;

namespace Echo.Core.Writes;

/// <summary>Only durable source facts are retained. Media and intermediate parser results stay out of SQLite.</summary>
internal static class ScanExecutionCheckpoint
{
    public const string Capability = "library.scanCheckpoints/v1";
    public const string PageBudgetCapability = "library.scanPageBudget/v1";
    public const string PageCursorCapability = "library.scanPageCursor/v1";
    // Match BoundedEncodeBuffer: default JSON escaping overestimates CUE titles and rejects valid pages.
    private static readonly CoreJsonContext PageJsonContext = new(new JsonSerializerOptions(CoreJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
    internal const string FileReceiptSql = """
        SELECT facts_json,identity,track_id,stage,rowid FROM core_scan_files
        WHERE job_id=$0 AND path=$1 LIMIT 1
        """;
    internal const string PendingPageSql = """
        SELECT facts_json,identity,track_id,stage,ordinal FROM (
          SELECT * FROM (
            SELECT rowid AS ordinal,facts_json,identity,track_id,stage FROM core_scan_files
            WHERE job_id=$0 AND stage='pending' AND rowid>$2 ORDER BY rowid LIMIT $1
          )
          UNION ALL
          SELECT * FROM (
            SELECT rowid AS ordinal,facts_json,identity,track_id,stage FROM core_scan_files
            WHERE job_id=$0 AND stage='reading' AND rowid>$2 ORDER BY rowid LIMIT $1
          )
        ) ORDER BY ordinal LIMIT $1
        """;
    public static bool Exists(TransactionSql sql, string? job) => job is not null
        && sql.Value("SELECT 1 FROM core_scan_tasks WHERE job_id=$0", job) is not null;

    public static ScanCheckpointResult Execute(TransactionSql sql, string job, ScanCheckpoint request, string now)
    {
        if(request.Limit is <1 or >64 || request.Cursor<0) throw new ArgumentException("Invalid scan page budget.");
        if (request.Action == "list")
        {
            var tasks = new List<ScanCheckpointTask>();
            using var command = sql.Command("""
                SELECT t.job_id,t.input_json,t.scan_timestamp,t.sealed,t.context_json,j.rowid
                FROM core_scan_tasks t JOIN scan_jobs j ON j.id=t.job_id
                WHERE j.status IN ('queued','running') AND j.cancel_requested=0 AND j.rowid>$0 ORDER BY j.rowid LIMIT 1
                """,request.Cursor);
            using var reader = command.ExecuteReader();
            var cursor=request.Cursor;
            while (reader.Read()) { tasks.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetInt64(3)!=0,reader.GetString(4))); cursor=reader.GetInt64(5); }
            return new(Tasks: tasks,Cursor:cursor);
        }
        ScanWriteGuard.RequireActive(sql, job);
        if (request.Action is "dispatchStart" or "dispatch" or "dispatchPage")
            return ScanExecutionDispatcher.Execute(sql, job, request);
        if (request.Action == "initialize")
        {
            if (request.Input is null || request.Input.Length > 1_048_576) throw new ArgumentException("Oversized scan input.");
            using var input = JsonDocument.Parse(request.Input);
            if (input.RootElement.GetProperty("version").GetInt32()!=1) throw new ArgumentException("Unsupported scan input.");
            var previous = sql.Value("SELECT input_json FROM core_scan_tasks WHERE job_id=$0",job);
            if (previous is not null && !Equals(previous,request.Input)) throw new ArgumentException("Scan input is immutable.");
            sql.Run("INSERT OR IGNORE INTO core_scan_tasks(job_id,input_json,scan_timestamp) VALUES($0,$1,$2)",job,request.Input,now);
        }
        else
        {
            if (!Exists(sql,job)) throw new ArgumentException("Durable scan input required.");
            var sealedTask = Convert.ToInt64(sql.Value("SELECT sealed FROM core_scan_tasks WHERE job_id=$0",job))!=0;
            if (request.Action == "files")
            {
                if (sealedTask || request.Files.Count>64 || request.Files.Sum(file=>(long)file.GetRawText().Length)>262_144)
                    throw new ArgumentException("Invalid manifest batch.");
                var added=0;
                foreach(var file in request.Files)
                {
                    var path=JsonFields.Text(file,"path") ?? throw new ArgumentException("File path required.");
                    if (path.Length>32768 || JsonFields.Text(file,"folderId")!=sql.Value("SELECT folder_id FROM scan_jobs WHERE id=$0",job)?.ToString())
                        throw new ArgumentException("Invalid scan file.");
                    var facts=file.GetRawText();
                    if (facts.Length>131_072) throw new ArgumentException("Oversized file facts.");
                    var identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(facts))).ToLowerInvariant();
                    var old=sql.Value("SELECT identity FROM core_scan_files WHERE job_id=$0 AND path=$1",job,path);
                    if(old is not null && !Equals(old,identity)) throw new ArgumentException("Conflicting file identity.");
                    var track=sql.Value("SELECT id FROM tracks WHERE path=$0",path)?.ToString() ?? Guid.NewGuid().ToString("D");
                    added+=sql.Run("INSERT OR IGNORE INTO core_scan_files(job_id,path,identity,facts_json,track_id) VALUES($0,$1,$2,$3,$4)",job,path,identity,facts,track);
                }
                sql.Run("UPDATE core_scan_tasks SET file_count=file_count+$1 WHERE job_id=$0",job,added);
            }
            else if(request.Action=="seal")
            {
                if(request.Context is null || request.Context.Length>262_144) throw new ArgumentException("Invalid scan context.");
                using var context=JsonDocument.Parse(request.Context);
                if(context.RootElement.TryGetProperty("initialFailures",out var failures)
                    && (!failures.TryGetInt64(out var count) || count<0 || count>1_000_000_000))
                    throw new ArgumentException("Invalid discovery failure count.");
                if(sealedTask && !Equals(sql.Value("SELECT context_json FROM core_scan_tasks WHERE job_id=$0",job),request.Context))
                    throw new ArgumentException("Scan context is immutable.");
                sql.Run("UPDATE core_scan_tasks SET sealed=1,context_json=$1 WHERE job_id=$0",job,request.Context);
                SynchronizeCounts(sql,job);
            }
            else if(request.Action=="settle" || request.Action=="reading")
            {
                if(!sealedTask || request.Paths.Count>64 || request.Stage is not ("skipped" or "failed" or "reading"))
                    throw new ArgumentException("Invalid scan file transition.");
                foreach(var path in request.Paths)
                {
                    if(path.Length>32768) throw new ArgumentException("Invalid scan path.");
                    var changed=sql.Run("UPDATE core_scan_files SET stage=$2,skipped=$3 WHERE job_id=$0 AND path=$1 AND stage IN ('pending','reading')",
                        job,path,request.Stage,request.Stage=="reading"?0:1);
                    if(changed>0 && request.Stage=="failed") sql.Run("""
                        UPDATE scan_jobs SET error_count=error_count+1,
                          errors_json=CASE WHEN json_array_length(errors_json)<200 THEN
                            json_insert(errors_json,'$[#]','File checkpoint failed; existing record preserved.') ELSE errors_json END WHERE id=$0
                        """,job);
                    if(changed>0 && request.Stage!="reading") Advance(sql,job,changed,changed);
                }
                SynchronizeCounts(sql,job);
            }
            else if(request.Action is not ("page" or "task")) throw new ArgumentException("Unknown scan checkpoint action.");
        }
        var files=new List<ScanCheckpointFile>();
        if(request.Action=="files")
            foreach(var file in request.Files) ReadFiles(sql,job,JsonFields.Text(file,"path"),files);
        else if(request.Action=="page") ReadFiles(sql,job,null,files,request.Limit,request.Cursor);
        List<ScanCheckpointTask>? task = null;
        if(request.Action=="task")
        {
            using var command=sql.Command("SELECT '',scan_timestamp,sealed,context_json FROM core_scan_tasks WHERE job_id=$0",job);
            using var reader=command.ExecuteReader();
            if(reader.Read()) task=[new(job,reader.GetString(0),reader.GetString(1),reader.GetInt64(2)!=0,reader.GetString(3))];
        }
        return new(Tasks:task,Files:files,Remaining:Remaining(sql,job),Cursor:request.Action=="page" && files.Count>0 ? files[^1].Ordinal : request.Cursor);
    }

    internal static void ReadFiles(TransactionSql sql,string job,string? path,List<ScanCheckpointFile> files,int limit=16,long cursor=0,long ceiling=long.MaxValue)
    {
        // A receipt is one primary-key lookup. Combining it with the page filter via OR
        // scans and sorts the growing manifest once for every file in each write batch.
        using var command=path is not null
            ? sql.Command(FileReceiptSql,job,path)
            : sql.Command(PendingPageSql,job,limit,cursor);
        using var reader=command.ExecuteReader();
        var bytes=0;
        while(reader.Read()) {
            var file=new ScanCheckpointFile(path is null ? reader.GetString(0) : "",reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetInt64(4));
            if (path is null && file.Ordinal > ceiling) break;
            var size=Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(file,PageJsonContext.ScanCheckpointFile));
            if(path is null && size>262_144) throw new InvalidOperationException("Scan checkpoint file exceeds page byte budget.");
            if(path is null && files.Count>0 && bytes+size>262_144) break;
            files.Add(file);bytes+=size;
        }
    }
    public static long Remaining(TransactionSql sql,string job)=>Convert.ToInt64(sql.Value(
        "SELECT file_count-processed_count FROM core_scan_tasks WHERE job_id=$0",job));
    private static void Advance(TransactionSql sql,string job,int processed,int skipped)=>sql.Run(
        "UPDATE core_scan_tasks SET processed_count=processed_count+$1,skipped_count=skipped_count+$2 WHERE job_id=$0",job,processed,skipped);
    public static void SynchronizeCounts(TransactionSql sql,string job)
    {
        sql.Run("""
            UPDATE scan_jobs SET total_files=t.file_count+COALESCE(json_extract(t.context_json,'$.initialFailures'),0),
              discovered_count=t.file_count+COALESCE(json_extract(t.context_json,'$.initialFailures'),0),
              processed_files=t.processed_count+COALESCE(json_extract(t.context_json,'$.initialFailures'),0),
              parsed_count=t.processed_count+COALESCE(json_extract(t.context_json,'$.initialFailures'),0),
              skipped_files=t.skipped_count+COALESCE(json_extract(t.context_json,'$.initialFailures'),0),
              skipped_count=t.skipped_count+COALESCE(json_extract(t.context_json,'$.initialFailures'),0)
              FROM core_scan_tasks t WHERE scan_jobs.id=t.job_id AND t.job_id=$0
            """,job);
    }
    public static void ValidateMutation(TransactionSql sql,LibraryMutation request)
    {
        if(!Exists(sql,request.ScanJobId) || request.Operation is not ("scan" or "repair")) return;
        if(Convert.ToInt64(sql.Value("SELECT sealed FROM core_scan_tasks WHERE job_id=$0",request.ScanJobId))!=1)
            throw new ArgumentException("Scan manifest must be sealed before writes.");
        var keys=request.Operation=="scan"?request.Items.Select(item=>(item.CheckpointKey,JsonFields.Text(item.Track,"path")))
            :request.Repairs.Select(item=>(item.CheckpointKey,sql.Value("SELECT path FROM tracks WHERE id=$0",item.TrackId)?.ToString()));
        foreach(var(key,path) in keys)
            if(key is null || path is null || sql.Value("SELECT 1 FROM core_scan_files WHERE job_id=$0 AND path=$1 AND identity=$2 AND stage IN ('pending','reading')",request.ScanJobId,path,key) is null)
                throw new ArgumentException("File checkpoint does not authorize this write.");
        var identities = request.Operation=="scan"?request.Items.Select(item=>item.CheckpointKey):request.Repairs.Select(item=>item.CheckpointKey);
        if(identities.Distinct().Count()!=identities.Count()) throw new ArgumentException("Duplicate file checkpoint in one batch.");
        var timestamp=sql.Value("SELECT scan_timestamp FROM core_scan_tasks WHERE job_id=$0",request.ScanJobId)?.ToString();
        if(request.Operation=="scan")
        {
            if(request.ScanTimestamp!=timestamp) throw new ArgumentException("Scan timestamp changed.");
            foreach(var item in request.Items)
            {
                var facts=sql.Value("SELECT facts_json FROM core_scan_files WHERE job_id=$0 AND identity=$1",request.ScanJobId,item.CheckpointKey)?.ToString();
                using var document=JsonDocument.Parse(facts!);
                foreach(var field in new[]{"folderId","sizeBytes","mtimeMs"})
                    if(document.RootElement.GetProperty(field).GetRawText()!=item.Track.GetProperty(field).GetRawText())
                        throw new ArgumentException("Scan file facts changed.");
            }
        }
        else if(request.Repairs.Any(item=>item.UpdatedAt!=timestamp)) throw new ArgumentException("Scan repair timestamp changed.");
    }
    public static void RecordMutation(TransactionSql sql,LibraryMutation request)
    {
        if(!Exists(sql,request.ScanJobId)) return;
        if(request.Operation is "scan" or "repair")
        {
            var keys=request.Operation=="scan"?request.Items.Select(item=>(item.CheckpointKey,false))
                :request.Repairs.Select(item=>(item.CheckpointKey,item.CheckpointSkipped));
            var processed=0;var skipped=0;
            foreach(var(key,skip) in keys) {
                var changed=sql.Run("UPDATE core_scan_files SET stage='committed',skipped=$2 WHERE job_id=$0 AND identity=$1 AND stage IN ('pending','reading')",
                    request.ScanJobId,key,skip?1:0);
                processed+=changed;if(skip)skipped+=changed;
            }
            Advance(sql,request.ScanJobId!,processed,skipped);
            SynchronizeCounts(sql,request.ScanJobId!);
        }
    }
    public static void Cleanup(TransactionSql sql,string job)
    {
        ScanExecutionDispatcher.Cleanup(sql, job);
        sql.Run("DELETE FROM core_scan_files WHERE job_id=$0",job);
        sql.Run("DELETE FROM core_scan_tasks WHERE job_id=$0",job);
        sql.Run("DELETE FROM core_scan_seen_paths WHERE scan_job_id=$0",job);
    }
}
