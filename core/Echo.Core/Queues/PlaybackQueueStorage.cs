using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Core.Protocol;
using Echo.Core.Database;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Queues;

/// <summary>Application-scoped durable data. No track objects or queue snapshots
/// survive a request; the connection's page cache is capped at 2 MiB.</summary>
public sealed partial class PlaybackQueueStorage : IDisposable
{
    public const string Capability = "playbackQueue.indexed/v1";
    public const string AuthorityCapability = "playbackQueue.authority/v2";
    private SqliteConnection? _connection;
    private string? _path;
    private SqliteTransaction? _transaction;
    private bool _migrationReady;
    private const string DefaultMode = """{"isShuffleEnabled":false,"repeatMode":"off","automixEnabled":false,"autoFillQueueEnabled":false,"continuousPlayMode":"similar","continuousPlayPreferences":[]}""";

    private SqliteConnection Open(PlaybackQueueRequest request, string libraryPath, CancellationToken cancellation)
    {
        var path=request.StorePath;
        if (_connection is not null && _path == path)
        {
            EnsureApplicationOwner(request);
            if(request.Read?.Operation=="releaseUnmigrated")return _connection;
            if (!_migrationReady)
            {
                try { MigrateLegacy(libraryPath,cancellation);_migrationReady=true; }
                catch { Close();throw; }
            }
            return _connection;
        }
        Close();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Concrete queue store path required.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path,Pooling=false,Mode=SqliteOpenMode.ReadWriteCreate }.ConnectionString);
        connection.Open();
        _connection=connection;_path=path;
        try
        {
            Run(PlaybackQueueSchema.Sql);
            EnsureApplicationOwner(request);
            if(request.Read?.Operation=="releaseUnmigrated")return connection;
            MigrateLegacy(libraryPath,cancellation);
            _migrationReady=true;
            return connection;
        }
        catch { Close();throw; }
    }
    public object Execute(PlaybackQueueRequest request, ReadOnlyLibraryDatabase library, CancellationToken cancellation, bool? commandExpected = null)
    {
        if ((request.Read is null) == (request.Command is null)
            || commandExpected is {} expected && expected != (request.Command is not null))
            throw new ArgumentException("Queue request must match its read/write method.");
        cancellation.ThrowIfCancellationRequested();
        var libraryPath=library.RequireConnection().DataSource;
        Open(request,libraryPath,cancellation);
        cancellation.ThrowIfCancellationRequested();
        if(request.Read?.Operation=="releaseUnmigrated")
        {
            ReleaseUnmigratedOwner();
            Close();
            return JsonSerializer.SerializeToElement(new { released=true });
        }
        var result = request.Command is {} command ? ExecuteCommand(command,libraryPath,cancellation)
            : Read(request.Read ?? new(),library,cancellation);
        return JsonSerializer.SerializeToElement(result);
    }
    public void Interrupt()
    {
        if (_connection?.Handle is {} handle) SQLitePCL.raw.sqlite3_interrupt(handle);
    }
    public void Close() { _connection?.Dispose();_connection=null;_path=null;_migrationReady=false; }
    public void Dispose()=>Close();

    private SqliteCommand Command(string sql, params object?[] values)
    {
        var command=_connection!.CreateCommand();command.CommandText=sql;
        command.Transaction=_transaction;
        for(var i=0;i<values.Length;i++)command.Parameters.AddWithValue("$p"+i,values[i]??DBNull.Value);
        return command;
    }
    private int Run(string sql, params object?[] values)
    { using var command=Command(sql,values);return command.ExecuteNonQuery(); }
    private object? Scalar(string sql, params object?[] values)
    { using var command=Command(sql,values);return command.ExecuteScalar() is {} value && value!=DBNull.Value ? value : null; }
    private const string Columns="queue_id,ordinal,track_id,library_path,item_json,track_json,title,artist,album,duration_seconds";
    private QueueTransaction Begin()=>new(this);
    private sealed class QueueTransaction : IDisposable
    {
        private readonly PlaybackQueueStorage _owner;
        public QueueTransaction(PlaybackQueueStorage owner) { _owner=owner;owner._transaction=owner._connection!.BeginTransaction(); }
        public void Commit()=>_owner._transaction!.Commit();
        public void Dispose() { _owner._transaction!.Dispose();_owner._transaction=null; }
    }
    private void CopyItems(string from,string to)
    {
        Run($"INSERT INTO core_queue_items(session_id,{Columns}) SELECT $p1,{Columns} FROM core_queue_items WHERE session_id=$p0",from,to);
        Run("INSERT INTO core_queue_track_memory SELECT $p1,track_json FROM core_queue_track_memory WHERE session_id=$p0",from,to);
    }
    private void RemoveSession(string id)
    { Run("DELETE FROM core_queue_items WHERE session_id=$p0",id);Run("DELETE FROM core_queue_track_memory WHERE session_id=$p0",id);Run("DELETE FROM core_queue_sessions WHERE id=$p0",id); }
    private static string? Text(JsonElement value,string name)=>value.TryGetProperty(name,out var property)&&property.ValueKind==JsonValueKind.String?property.GetString():null;
    private static readonly string[] RecoveryFields=["id","mediaType","path","title","artist","album","albumArtist","duration","sampleRate","bitDepth","bitrate","codec","coverId","coverThumb","fieldSources","sourceId","sourceDisplayName","provider","providerTrackId","remotePath","stableKey","isTemporary","unavailable",
        "isLiveStream","streamingQuality","liveVideoUrl","trackNo","discNo","year","genre","mqa","bpm","bpmEstimated","bpmConfidence","beatOffsetMs","hasOsuStoryboard",
        "replayGainTrackGainDb","replayGainAlbumGainDb","replayGainTrackPeak","replayGainAlbumPeak","replayGainIntegratedLufs","replayGainSource","replayGainStatus","replayGainUpdatedAt","hasReplayGain","playlistItemId","playlistPlaybackPage"];
    private void InsertItem(string sessionId,long ordinal,JsonElement item,string libraryPath)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(item.GetRawText())>65536)throw new ArgumentException("Queue item exceeds its recovery bound.");
        var queueId=Text(item,"queueId")??throw new ArgumentException("Queue occurrence ID required.");
        var track=item.GetProperty("track");var trackId=Text(track,"id")??throw new ArgumentException("Queue track ID required.");
        var snapshot=new JsonObject();foreach(var field in RecoveryFields)if(track.TryGetProperty(field,out var value))snapshot[field]=JsonNode.Parse(value.GetRawText());
        snapshot["fieldSources"]??=new JsonObject();
        var facts=JsonNode.Parse(item.GetRawText())!.AsObject();facts.Remove("track");
        var duration=track.TryGetProperty("duration",out var seconds)&&seconds.TryGetDouble(out var numeric)?Math.Max(0,numeric):0;
        Run("""
            INSERT INTO core_queue_items(session_id,queue_id,ordinal,track_id,library_path,item_json,track_json,title,artist,album,duration_seconds)
              VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10)
            """,sessionId,queueId,ordinal,trackId,Text(track,"mediaType")=="remote"?null:libraryPath,
            facts.ToJsonString(),snapshot.ToJsonString(),Text(track,"title")??"",Text(track,"artist")??"",Text(track,"album")??"",duration);
    }
}
