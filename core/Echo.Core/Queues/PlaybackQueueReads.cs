using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query;
using Echo.Core.Search;

namespace Echo.Core.Queues;

public sealed partial class PlaybackQueueStorage
{
    private JsonObject Summary()
    {
        using var command=Command("""
            SELECT session_token,revision,structure_revision,pointer_revision,total,duration_seconds,
              current_queue_id,current_track_id,mode_json,resume_json,undo_token,
              COALESCE((SELECT ordinal FROM core_queue_items WHERE session_id='active'
                  AND queue_id=session.current_queue_id),-1) AS current_index
            FROM core_queue_sessions AS session WHERE id='active'
            """);
        using var reader=command.ExecuteReader();
        if(!reader.Read())throw new InvalidOperationException("Active queue session missing.");
        string? Text(int i)=>reader.IsDBNull(i)?null:reader.GetString(i);
        var result=new JsonObject { ["version"]=2,["sessionId"]=Text(0),["revision"]=reader.GetInt64(1),
            ["structureRevision"]=reader.GetInt64(2),["pointerRevision"]=reader.GetInt64(3),["total"]=reader.GetInt64(4),
            ["durationSeconds"]=reader.GetDouble(5),["currentQueueId"]=Text(6),["currentTrackId"]=Text(7),
            ["mode"]=JsonNode.Parse(Text(8)!),["resume"]=Text(9) is {} resume?JsonNode.Parse(resume):null,
            ["undoToken"]=Text(10),["currentIndex"]=Text(6) is null?-1:reader.GetInt64(11) };
        reader.Close();
        result["temporary"]=Scalar("SELECT descriptor_json FROM core_queue_temporary WHERE id=1") is string temporary?JsonNode.Parse(temporary):null;
        result["lastPlayedTrack"]=Scalar("SELECT track_json FROM core_queue_track_memory WHERE session_id='active'") is string last?JsonNode.Parse(last):null;
        result["selectionToken"]=StoredText("SELECT token FROM core_queue_selection WHERE id=1");
        result["selectionTotal"]=Convert.ToInt64(Scalar("SELECT total FROM core_queue_selection WHERE id=1")??0);
        result["selectionFlagged"]=Convert.ToInt64(Scalar("SELECT flagged FROM core_queue_selection WHERE id=1")??0);
        result["selectionRevision"]=Convert.ToInt64(Scalar("SELECT revision FROM core_queue_selection WHERE id=1")??0);
        return result;
    }
    private JsonNode Read(PlaybackQueueRead read,ReadOnlyLibraryDatabase library,CancellationToken cancellation)
    {
        if(read.Operation is not ("claim" or "summary" or "page" or "item" or "neighborhood" or "backendPage" or "saved" or "receipt" or "target" or "nextUpAnchor" or "appendAnchor" or "history" or "reference"))
            throw new ArgumentException("Unknown queue read operation.");
        var summary=Summary();
        if(read.Operation=="receipt")return Scalar("SELECT result_json FROM core_queue_receipts WHERE command_id=$p0",read.CommandId) is string receipt
            ? JsonNode.Parse(receipt)! : new JsonObject{["committed"]=false,["summary"]=summary};
        if(read.SessionId is {} token && token!=summary["sessionId"]!.GetValue<string>()
            || read.Revision is {} revision && revision!=summary["revision"]!.GetValue<long>())
            return new JsonObject{["stale"]=true,["summary"]=summary};
        if(read.Operation is "summary" or "claim")return summary;
        if(read.Operation=="reference")return new JsonObject{["references"]=Convert.ToInt64(Scalar("SELECT (SELECT COUNT(*) FROM core_queue_items WHERE track_id=$p0)+(SELECT COUNT(*) FROM core_queue_track_memory WHERE json_extract(track_json,'$.id')=$p0)+(SELECT COUNT(*) FROM core_queue_sessions WHERE json_extract(resume_json,'$.trackId')=$p0 OR EXISTS(SELECT 1 FROM json_each(history_json) WHERE json_extract(value,'$.track.id')=$p0))",read.TrackId)??0)>0};
        if(read.Operation=="saved")
        {
            var saved=new JsonArray();using var command=Command("SELECT id,name,total,updated_at FROM core_queue_sessions WHERE kind='saved' ORDER BY updated_at DESC LIMIT 12");
            using var reader=command.ExecuteReader();while(reader.Read())saved.Add(new JsonObject{
                ["id"]=reader.GetString(0),["name"]=reader.GetString(1),["trackCount"]=reader.GetInt64(2),["createdAt"]=reader.GetString(3)});
            return saved;
        }
        var offset=Math.Max(0,read.Offset);var limit=Math.Clamp(read.Limit,1,256);
        var filter="session_id='active'";var parameters=new List<object?>();
        if(read.SavedId is {} savedSession)
        {
            if(Scalar("SELECT id FROM core_queue_sessions WHERE id=$p0 AND kind='saved'",savedSession) is null)throw new InvalidOperationException("Saved queue is stale.");
            filter="session_id=$p0";parameters.Add(savedSession);
            summary["total"]=Convert.ToInt64(Scalar("SELECT total FROM core_queue_sessions WHERE id=$p0",savedSession));
            summary["durationSeconds"]=Convert.ToDouble(Scalar("SELECT duration_seconds FROM core_queue_sessions WHERE id=$p0",savedSession));
        }
        if(read.SelectionOnly)filter+=" AND queue_id IN (SELECT queue_id FROM core_queue_selection_items)";
        var anchorRead=read.Operation is "nextUpAnchor" or "appendAnchor";
        if(anchorRead)read.QueueId=StoredText("SELECT queue_id FROM core_queue_items WHERE session_id='active' AND ordinal>$p0 AND "+
            (read.Operation=="nextUpAnchor"?"COALESCE(json_extract(item_json,'$.queuedNext'),0)!=1":"json_extract(item_json,'$.source.type')='continuous-play'")+" ORDER BY ordinal LIMIT 1",summary["currentIndex"]!.GetValue<long>());
        if(read.Operation=="target")read.QueueId=SelectTarget(read,summary);
        if(read.Operation is "item" or "target" || anchorRead)
        {
            if(read.QueueId is not null || read.Operation=="target" || anchorRead) { filter+=" AND queue_id=$p"+parameters.Count;parameters.Add(read.QueueId); }
            else if(read.TrackId is not null) { filter+=" AND track_id=$p"+parameters.Count;parameters.Add(read.TrackId); }
            else { filter+=" AND json_extract(track_json,'$.path')=$p"+parameters.Count;parameters.Add(read.FilePath); }
            offset=0;limit=1;
        }
        if(read.Operation=="backendPage" && read.Shuffle && BaseAlbumSource(read.FromQueueId??summary["currentQueueId"]?.GetValue<string>()) is {} backendAlbum)
        {
            filter+=" AND (json_extract(item_json,'$.source.albumId')=$p"+parameters.Count+" OR (ordinal>$p"+(parameters.Count+1)+" AND json_extract(item_json,'$.queuedNext')=1) OR queue_id=$p"+(parameters.Count+2)+")";
            var backendCurrent=read.FromQueueId??summary["currentQueueId"]?.GetValue<string>();
            parameters.Add(backendAlbum);parameters.Add(Convert.ToInt64(Scalar("SELECT ordinal FROM core_queue_items WHERE session_id='active' AND queue_id=$p0",backendCurrent)??-1));parameters.Add(backendCurrent);
        }
        else if(read.Operation=="neighborhood")
        {
            var index=read.QueueId is {} id ? Convert.ToInt32(Scalar("SELECT ordinal FROM core_queue_items WHERE session_id='active' AND queue_id=$p0",id)??0)
                : Math.Max(0,checked((int)summary["currentIndex"]!.GetValue<long>()));
            offset=Math.Max(0,index-4);limit=12;
        }
        if(read.Display && summary["currentIndex"]!.GetValue<long>()>=0)
        {
            var current=summary["currentIndex"]!.GetValue<long>();
            var album=BaseAlbumSource(summary["currentQueueId"]?.GetValue<string>());
            filter+=" AND (ordinal>=$p"+parameters.Count;parameters.Add(current);
            if(album is not null)
            {
                filter+=" OR (COALESCE(json_extract(item_json,'$.queuedNext'),0)!=1 AND json_extract(item_json,'$.source.type')='album' AND json_extract(item_json,'$.source.albumId')=$p"+parameters.Count+")";parameters.Add(album);
            }
            filter+=")";
            if(read.Cursor is null) { var preceding=filter+" AND ordinal<$p"+parameters.Count;
                summary["displayCurrentIndex"]=Convert.ToInt64(Scalar("SELECT COUNT(*) FROM core_queue_items WHERE "+preceding,[..parameters,current])); }
        }
        var unfiltered=!read.SelectionOnly && !read.Display && string.IsNullOrWhiteSpace(read.Search) && read.Operation is ("page" or "backendPage" or "neighborhood");
        var resultOffset=offset;
        if(read.Cursor is {} cursor)
        {
            filter+=" AND ordinal>$p"+parameters.Count;parameters.Add(cursor);offset=0;
        }
        else if(unfiltered)
        {
            filter+=" AND ordinal>=$p"+parameters.Count;parameters.Add(offset);offset=0;
        }
        if(!string.IsNullOrWhiteSpace(read.Search))
        {
            var normalized=LibrarySearchNormalizer.Entity(read.Search);
            foreach(var term in normalized.Terms)
            {
                var clauses=new List<string>();
                foreach(var variant in term.Variants)
                {
                    var escaped="%"+variant.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
                    foreach(var column in new[]{"title","artist","album","json_extract(track_json,'$.albumArtist')","json_extract(item_json,'$.source.label')"})
                    { clauses.Add(column+" LIKE $p"+parameters.Count+" ESCAPE '\\'");parameters.Add(escaped); }
                }
                if(clauses.Count>0)filter+=" AND ("+string.Join(" OR ",clauses)+")";
            }
        }
        if(read.Operation=="history")filter+=" AND 0";
        var matched=!read.SelectionOnly && (read.Cursor is not null || !read.Display) && string.IsNullOrWhiteSpace(read.Search) && read.Operation is not ("item" or "target")?summary["total"]!.GetValue<long>()
            :Convert.ToInt64(Scalar("SELECT COUNT(*) FROM core_queue_items WHERE "+filter,parameters.ToArray()));
        if(read.Operation=="history")matched=Convert.ToInt64(Scalar("SELECT json_array_length(history_json) FROM core_queue_sessions WHERE id='active'"));
        if(read.Operation=="page" && read.Cursor is null)
            summary["selectionMatchedTotal"]=Convert.ToInt64(Scalar("SELECT COUNT(*) FROM core_queue_items WHERE "+filter+" AND queue_id IN (SELECT queue_id FROM core_queue_selection_items)",parameters.ToArray()));
        if(read.Display && read.Cursor is null)summary["displayCurrentIndex"]??=JsonValue.Create(0L);
        // The displayed tail is contiguous after any earlier album prefix.
        // Seek by ordinal instead of stepping through every preceding index row.
        if(read.Display && !read.SelectionOnly && string.IsNullOrWhiteSpace(read.Search)
            && read.Cursor is null && summary["displayCurrentIndex"] is {} prefix
            && offset>=prefix.GetValue<long>())
        {
            var start=Math.Max(0,summary["currentIndex"]!.GetValue<long>())+offset-prefix.GetValue<long>();
            filter+=" AND ordinal>=$p"+parameters.Count;parameters.Add(start);offset=0;
        }
        var rows=new List<(string Facts,string Snapshot,string? Library,string TrackId)>();
        long? nextCursor=null;
        var sql="SELECT item_json,track_json,library_path,track_id,ordinal FROM core_queue_items WHERE "+filter+" ORDER BY ordinal,queue_id LIMIT $p"+parameters.Count+" OFFSET $p"+(parameters.Count+1);
        parameters.Add(limit);parameters.Add(offset);
        using(var command=Command(sql,parameters.ToArray()))using(var reader=command.ExecuteReader())
            while(reader.Read()) { cancellation.ThrowIfCancellationRequested();rows.Add((reader.GetString(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetString(3)));nextCursor=reader.GetInt64(4); }
        if(read.Operation=="history" || read.Operation=="target" && rows.Count==0 && read.Direction<0 && PreviousUsesHistory(read,summary))
        {
            rows.Clear();
            using var history=Command("SELECT value FROM core_queue_sessions,json_each(history_json) WHERE core_queue_sessions.id='active'"+
                (read.Operation=="target"?" AND COALESCE(json_extract(value,'$.track.unavailable'),0)!=1":"")+
                " ORDER BY CAST(key AS INTEGER) DESC LIMIT $p0 OFFSET $p1",read.Operation=="target"?1:limit,offset);
            using var previous=history.ExecuteReader();
            while(previous.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                var item=JsonNode.Parse(previous.GetString(0))!.AsObject();
                var track=item["track"]!.ToJsonString();var trackId=item["track"]!["id"]!.GetValue<string>();item.Remove("track");
                rows.Add((item.ToJsonString(),track,null,trackId));
            }
        }
        if(read.Operation=="item" && nextCursor is {} itemOrdinal)resultOffset=checked((int)itemOrdinal);
        if(read.Display && read.Cursor is null)
            summary["remainingDurationSeconds"]=Convert.ToDouble(Scalar("SELECT COALESCE(SUM(duration_seconds),0) FROM core_queue_items WHERE session_id='active' AND ordinal>$p0",summary["currentIndex"]!.GetValue<long>()));
        var libraryPath=library.RequireConnection().DataSource;
        var ids=rows.Where(row=>row.Library==libraryPath).Select(row=>row.TrackId).Distinct().ToArray();
        var tracks=TrackByIdQuery.Execute(library,ids).ToDictionary(track=>track.Id,StringComparer.Ordinal);
        var items=new JsonArray();
        foreach(var row in rows)
        {
            cancellation.ThrowIfCancellationRequested();
            var item=JsonNode.Parse(row.Facts)!.AsObject();
            item["track"]=row.Library==libraryPath && tracks.TryGetValue(row.TrackId,out var track)
                ? JsonSerializer.SerializeToNode(track,CoreJsonContext.Default.LibraryTrackDto) : JsonNode.Parse(row.Snapshot);
            if(row.Library==libraryPath && tracks.ContainsKey(row.TrackId))
            {
                using var recovery=JsonDocument.Parse(row.Snapshot);
                foreach(var field in new[]{"playlistItemId","playlistPlaybackPage","isTemporary"})
                    if(recovery.RootElement.TryGetProperty(field,out var value))item["track"]![field]=JsonNode.Parse(value.GetRawText());
            }
            items.Add(item);
        }
        if(read.Operation=="page" && summary["selectionToken"] is not null)
        {
            var selected=new JsonArray();
            using var command=Command("SELECT queue_id FROM core_queue_selection_items WHERE queue_id IN (SELECT value FROM json_each($p0))",JsonSerializer.Serialize(items.Select(item=>item?["queueId"]?.GetValue<string>()).ToArray()));
            using var reader=command.ExecuteReader();while(reader.Read())selected.Add(reader.GetString(0));summary["selectedQueueIds"]=selected;
        }
        summary["offset"]=resultOffset;summary["matchedTotal"]=matched;summary["items"]=items;summary["nextCursor"]=nextCursor;
        return summary;
    }
}
