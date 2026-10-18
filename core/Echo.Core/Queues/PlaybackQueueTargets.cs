using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Core.Protocol;

namespace Echo.Core.Queues;

public sealed partial class PlaybackQueueStorage
{
    private string? StoredText(string sql,params object?[] values)=>Scalar(sql,values) as string;
    private string? BaseAlbumSource(string? current)
    {
        // A queued-next occurrence borrows the nearest ordinary source, just
        // as the existing renderer navigation policy does.
        var position=Convert.ToInt64(Scalar("SELECT ordinal FROM core_queue_items WHERE session_id='active' AND queue_id=$p0",current)??-1);
        return StoredText("""
            SELECT CASE WHEN json_extract(item_json,'$.source.type')='album' THEN json_extract(item_json,'$.source.albumId') END
            FROM core_queue_items WHERE session_id='active' AND queue_id=COALESCE(
              (SELECT queue_id FROM core_queue_items WHERE session_id='active' AND ordinal<=$p0 AND COALESCE(json_extract(item_json,'$.queuedNext'),0)!=1 ORDER BY ordinal DESC LIMIT 1),
              (SELECT queue_id FROM core_queue_items WHERE session_id='active' AND ordinal>$p0 AND COALESCE(json_extract(item_json,'$.queuedNext'),0)!=1 ORDER BY ordinal LIMIT 1))
            """,position);
    }
    private bool PreviousUsesHistory(PlaybackQueueRead read,JsonObject summary)
    {
        var current=read.FromQueueId??summary["currentQueueId"]?.GetValue<string>();
        return current is null || read.Shuffle || BaseAlbumSource(current) is null || Convert.ToInt64(Scalar("SELECT COALESCE(json_extract(item_json,'$.queuedNext'),0) FROM core_queue_items WHERE session_id='active' AND queue_id=$p0",current)??0)==1;
    }
    private string? SelectTarget(PlaybackQueueRead read,JsonObject summary)
    {
        var current=read.FromQueueId??summary["currentQueueId"]?.GetValue<string>();
        var position=Convert.ToInt64(Scalar("SELECT ordinal FROM core_queue_items WHERE session_id='active' AND queue_id=$p0",current)??-1);
        var available="COALESCE(json_extract(track_json,'$.unavailable'),0)!=1 AND COALESCE(json_extract(track_json,'$.path'),'')!=''";
        if((read.ExcludeQueueIds?.Count??0)>4)throw new ArgumentException("Queue plan exceeds its bound.");
        if(read.ExcludeQueueIds is {Count:>0} excluded)
        {
            // IDs are quoted as SQL literals only after length/control validation;
            // this clause contains at most four application-generated occurrence IDs.
            if(excluded.Any(id=>id.Length>128 || id.Any(char.IsControl)))throw new ArgumentException("Invalid queue plan ID.");
            available+=" AND queue_id NOT IN ("+string.Join(",",excluded.Select(id=>"'"+id.Replace("'","''")+"'"))+")";
        }
        if(read.Direction<0 && PreviousUsesHistory(read,summary))
        {
            return StoredText("SELECT json_extract(value,'$.queueId') FROM core_queue_sessions,json_each(history_json) WHERE core_queue_sessions.id='active' AND COALESCE(json_extract(value,'$.track.unavailable'),0)!=1 ORDER BY CAST(key AS INTEGER) DESC LIMIT 1");
        }
        if(read.Direction>=0)
        {
            var nextUp=StoredText($"SELECT queue_id FROM core_queue_items WHERE session_id='active' AND ordinal>$p0 AND ordinal<COALESCE((SELECT MIN(ordinal) FROM core_queue_items WHERE session_id='active' AND ordinal>$p0 AND COALESCE(json_extract(item_json,'$.queuedNext'),0)!=1),9223372036854775807) AND json_extract(item_json,'$.queuedNext')=1 AND {available} ORDER BY ordinal LIMIT 1",position);
            if(nextUp is not null)return nextUp;
        }
        var mode=summary["mode"]!.AsObject();
        if(read.Shuffle)
        {
            if(read.ExcludeQueueIds is {Count:>0} planned)
            {
                var literals=string.Join(",",planned.Select(id=>"'"+id.Replace("'","''")+"'"));
                available+=" AND track_id NOT IN (SELECT track_id FROM core_queue_items WHERE session_id='active' AND queue_id IN ("+literals+"))";
            }
            var source=BaseAlbumSource(current);
            var scope=source is null?"":" AND json_extract(item_json,'$.source.albumId')=$p2";
            var history=Math.Clamp(read.AvoidRecentCount,0,500);
            var sql=$"""
                SELECT queue_id FROM core_queue_items WHERE session_id='active' AND queue_id!=$p0 AND {available}{scope}
                  AND track_id NOT IN (SELECT json_extract(value,'$.track.id') FROM core_queue_sessions,json_each(history_json)
                    WHERE core_queue_sessions.id='active' ORDER BY CAST(key AS INTEGER) DESC LIMIT $p1)
                ORDER BY RANDOM() LIMIT 1
                """;
            var selected=source is null?StoredText(sql,current??"",history):StoredText(sql,current??"",history,source);
            if(selected is not null)return selected;
            if(source is not null && mode["repeatMode"]?.GetValue<string>()!="all")return null;
            return (source is null?StoredText($"SELECT queue_id FROM core_queue_items WHERE session_id='active' AND queue_id!=$p0 AND {available} ORDER BY RANDOM() LIMIT 1",current??"")
                    :StoredText($"SELECT queue_id FROM core_queue_items WHERE session_id='active' AND queue_id!=$p0 AND {available} AND json_extract(item_json,'$.source.albumId')=$p1 ORDER BY RANDOM() LIMIT 1",current??"",source))
                ??current;
        }
        var direction=read.Direction<0?"<":">";
        var order=read.Direction<0?"DESC":"ASC";
        if(read.Direction<0 && BaseAlbumSource(current) is {} previousAlbum)
            return StoredText($"SELECT queue_id FROM core_queue_items WHERE session_id='active' AND ordinal<$p0 AND COALESCE(json_extract(item_json,'$.queuedNext'),0)!=1 AND json_extract(item_json,'$.source.type')='album' AND json_extract(item_json,'$.source.albumId')=$p1 AND {available} ORDER BY ordinal DESC LIMIT 1",position,previousAlbum);
        var target=StoredText($"SELECT queue_id FROM core_queue_items WHERE session_id='active' AND ordinal{direction}$p0 AND {available} ORDER BY ordinal {order} LIMIT 1",position);
        if(target is not null)return target;
        if(mode["repeatMode"]?.GetValue<string>()=="all")return StoredText($"SELECT queue_id FROM core_queue_items WHERE session_id='active' AND {available} ORDER BY ordinal {order} LIMIT 1");
        return null;
    }

    private void RecordPreviousOccurrence(string? previous,string? current)
    {
        if(previous is null || previous==current)return;
        Run("""
            UPDATE core_queue_sessions SET history_json=(
              SELECT json_group_array(json(value)) FROM (
                SELECT value FROM (SELECT key,value FROM json_each(history_json) ORDER BY CAST(key AS INTEGER) DESC LIMIT 499) ORDER BY CAST(key AS INTEGER)
              )) WHERE id='active'
            """);
        Run("""
            UPDATE core_queue_sessions SET history_json=json_insert(history_json,'$[#]',json((
              SELECT json_set(item_json,'$.track',json(track_json)) FROM core_queue_items WHERE session_id='active' AND queue_id=$p0)))
            WHERE id='active' AND EXISTS(SELECT 1 FROM core_queue_items WHERE session_id='active' AND queue_id=$p0)
            """,previous);
    }

    private bool? ExecuteAdditionalEdit(PlaybackQueueCommand input)
    {
        if(ExecuteSelection(input) is {} selection)return selection;
        switch(input.Operation)
        {
            case "reorder":Move(input.QueueIds??[],null,ordered:true,front:true);return true;
            case "temporaryEnd":
                if(Scalar("SELECT id FROM core_queue_sessions WHERE id='original'") is null)return false;
                CloneSession("original","active","active");RemoveSession("original");Run("DELETE FROM core_queue_temporary");return true;
            case "flags":
                if(input.Flags is not {} flags || flags.ValueKind!=JsonValueKind.Object || flags.GetRawText().Length>256)throw new ArgumentException("Bounded queue flags required.");
                foreach(var property in flags.EnumerateObject())
                    if(property.Name is not ("removeAfterPlay" or "queuedNext") || property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Invalid queue flag.");
                Run("UPDATE core_queue_items SET item_json=json_patch(item_json,$p0) WHERE session_id='active' AND queue_id IN (SELECT value FROM json_each($p1))",flags.GetRawText(),JsonSerializer.Serialize(input.QueueIds??[]));return true;
            case "deleteTrack":
                Run("""
                    UPDATE core_queue_sessions SET
                      current_queue_id=CASE WHEN current_track_id=$p0 THEN NULL ELSE current_queue_id END,
                      current_track_id=CASE WHEN current_track_id=$p0 THEN NULL ELSE current_track_id END,
                      resume_json=CASE WHEN json_extract(resume_json,'$.trackId')=$p0 THEN NULL ELSE resume_json END,
                      history_json=(SELECT json_group_array(json(value)) FROM json_each(history_json) WHERE json_extract(value,'$.track.id')!=$p0)
                    WHERE id='active'
                    """,input.TrackId);
                Run("DELETE FROM core_queue_track_memory WHERE session_id='active' AND json_extract(track_json,'$.id')=$p0",input.TrackId);
                Run("DELETE FROM core_queue_items WHERE session_id='active' AND track_id=$p0",input.TrackId);Move([],null);return true;
            case "deleteRecommendations":
                Run("DELETE FROM core_queue_items WHERE session_id='active' AND json_extract(item_json,'$.source.type')='continuous-play' AND queue_id!=(SELECT COALESCE(current_queue_id,'') FROM core_queue_sessions WHERE id='active')");Move([],null);return true;
            case "historyBack":
                Run("UPDATE core_queue_sessions SET history_json=json_remove(history_json,'$[#-1]') WHERE id='active'");return false;
            case "deleteSaved":
                if(input.StagingId is {} saved && Scalar("SELECT id FROM core_queue_sessions WHERE id=$p0 AND kind='saved'",saved) is not null)RemoveSession(saved);
                return false;
            default:return null;
        }
    }
}
