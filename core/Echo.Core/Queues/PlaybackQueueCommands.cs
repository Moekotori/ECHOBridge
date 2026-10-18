using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Core.Protocol;

namespace Echo.Core.Queues;

public sealed partial class PlaybackQueueStorage
{
    private JsonNode ExecuteCommand(PlaybackQueueCommand input,string libraryPath,CancellationToken cancellation)
    {
        if(input.CommandId.Length is <1 or >128 || (input.Items?.Count??0)>256 || (input.QueueIds?.Count??0)>256)
            throw new ArgumentException("Queue command exceeds its batch limit.");
        var bytes=(input.Items??[]).Sum(item=>(long)System.Text.Encoding.UTF8.GetByteCount(item.GetRawText()))
            +(input.QueueIds??[]).Sum(id=>(long)System.Text.Encoding.UTF8.GetByteCount(id));
        if(bytes>1024*1024 || input.Resume is {} resume && resume.GetRawText().Length>16384)
            throw new ArgumentException("Queue command exceeds its byte limit.");
        foreach(var value in new[]{input.Mode,input.Patch})
            if(value is {} json && (json.ValueKind!=JsonValueKind.Object || System.Text.Encoding.UTF8.GetByteCount(json.GetRawText())>16384))
                throw new ArgumentException("Bounded queue metadata object required.");
        if(Scalar("SELECT result_json FROM core_queue_receipts WHERE command_id=$p0",input.CommandId) is string receipt)
            return JsonNode.Parse(receipt)!;
        using var transaction=Begin();
        var before=Summary();
        if(input.SessionId!=before["sessionId"]!.GetValue<string>() || input.ExpectedRevision!=before["revision"]!.GetValue<long>())
            return new JsonObject{["committed"]=false,["conflict"]=true,["summary"]=before};
        string? staging=null;string? undo=null;var structure=false;var mode=false;var metadata=false;
        switch(input.Operation)
        {
            case "pointer":
                var previousQueue=before["currentQueueId"]?.GetValue<string>();
                if(input.RecordHistory)RecordPreviousOccurrence(before["currentQueueId"]?.GetValue<string>(),input.CurrentQueueId);
                Run("""
                    INSERT INTO core_queue_track_memory SELECT 'active',track_json FROM core_queue_items WHERE session_id='active' AND queue_id=$p0
                    ON CONFLICT(session_id) DO UPDATE SET track_json=excluded.track_json
                    """,input.CurrentQueueId);
                Run("""
                    UPDATE core_queue_sessions SET current_queue_id=(SELECT queue_id FROM core_queue_items WHERE session_id='active' AND queue_id=$p0),
                      current_track_id=$p1,resume_json=$p2,pointer_revision=pointer_revision+1,updated_at=$p3 WHERE id='active'
                    """,input.CurrentQueueId,input.CurrentTrackId,input.Resume?.GetRawText(),DateTime.UtcNow.ToString("O"));
                if(previousQueue is not null && previousQueue!=input.CurrentQueueId && Convert.ToInt64(Scalar("SELECT COUNT(*) FROM core_queue_items WHERE session_id='active' AND queue_id=$p0 AND (json_extract(item_json,'$.removeAfterPlay')=1 OR (json_extract(item_json,'$.queuedNext')=1 AND $p1=1))",previousQueue,input.RecordHistory?1:0))>0)
                {
                    if(Convert.ToInt64(Scalar("SELECT COALESCE(json_extract(item_json,'$.removeAfterPlay'),0) FROM core_queue_items WHERE session_id='active' AND queue_id=$p0",previousQueue))>0)
                        Run("UPDATE core_queue_sessions SET history_json=(SELECT json_group_array(json(value)) FROM json_each(history_json) WHERE json_extract(value,'$.queueId')!=$p0) WHERE id='active'",previousQueue);
                    Run("DELETE FROM core_queue_items WHERE session_id='active' AND queue_id=$p0",previousQueue);Move([],null);structure=true;
                }
                break;
            case "mode":
                if(input.Mode is not {} newMode || newMode.GetRawText().Length>16384)throw new ArgumentException("Bounded queue mode required.");
                Run("UPDATE core_queue_sessions SET mode_json=$p0 WHERE id='active'",newMode.GetRawText());mode=true;break;
            case "checkpoint":
                undo=Guid.NewGuid().ToString("N");CloneSession("active","undo","undo");
                Run("DELETE FROM core_queue_undo_selection_items;INSERT INTO core_queue_undo_selection_items SELECT queue_id FROM core_queue_selection_items");
                Run("UPDATE core_queue_sessions SET undo_token=$p0 WHERE id='active'",undo);break;
            case "undo":
                if(input.UndoToken is null || input.UndoToken!=Convert.ToString(Scalar("SELECT undo_token FROM core_queue_sessions WHERE id='active'")))
                    throw new InvalidOperationException("Queue undo token is stale.");
                CloneSession("undo","active","active");RemoveSession("undo");
                // Queue undo restores occurrences, never an old host pointer,
                // resume position or a playback mode changed since checkpoint.
                Run("""
                    UPDATE core_queue_sessions SET current_queue_id=$p0,current_track_id=$p1,
                      resume_json=$p2,mode_json=$p3,pointer_revision=$p4,history_json='[]' WHERE id='active'
                    """,before["currentQueueId"]?.GetValue<string>(),before["currentTrackId"]?.GetValue<string>(),
                    before["resume"]?.ToJsonString(),before["mode"]!.ToJsonString(),before["pointerRevision"]!.GetValue<long>());
                Run("DELETE FROM core_queue_track_memory WHERE session_id='active'");
                if(before["lastPlayedTrack"] is {} playing)Run("INSERT INTO core_queue_track_memory VALUES('active',$p0)",playing.ToJsonString());
                structure=true;break;
            case "replaceBegin":
                if(Convert.ToInt64(Scalar("SELECT COUNT(*) FROM core_queue_sessions WHERE kind='staging'")??0)>=2)
                    throw new InvalidOperationException("Queue replacement staging is already occupied.");
                staging="staging:"+Guid.NewGuid().ToString("N");NewSession(staging,"staging",mode:input.Mode?.GetRawText()??before["mode"]!.ToJsonString());break;
            case "replaceCommit":
                var replacementHistory=StoredText("SELECT history_json FROM core_queue_sessions WHERE id='active'")??"[]";
                var beginningTemporary=input.Temporary is not null && Scalar("SELECT id FROM core_queue_sessions WHERE id='original'") is null;
                if(input.StagingId is null || Scalar("SELECT id FROM core_queue_sessions WHERE id=$p0 AND kind='staging'",input.StagingId) is null)
                    throw new InvalidOperationException("Unknown queue replacement.");
                if(input.Temporary is {} temporary)
                {
                    if(temporary.ValueKind!=JsonValueKind.Object || temporary.GetRawText().Length>16384)throw new ArgumentException("Bounded temporary queue descriptor required.");
                    if(Scalar("SELECT id FROM core_queue_sessions WHERE id='original'") is null)CloneSession("active","original","original");
                    Run("INSERT INTO core_queue_temporary VALUES(1,$p0) ON CONFLICT(id) DO UPDATE SET descriptor_json=excluded.descriptor_json",temporary.GetRawText());
                }
                CloneSession(input.StagingId,"active","active");RemoveSession(input.StagingId);
                PreservePlaybackHeader(before,beginningTemporary?"[]":replacementHistory);
                if(input.Temporary is not null)Run("UPDATE core_queue_sessions SET mode_json=json_set(mode_json,'$.repeatMode','off') WHERE id='active'");
                Run("UPDATE core_queue_sessions SET undo_token=$p0 WHERE id='active'",before["undoToken"]?.GetValue<string>());
                structure=true;break;
            case "replaceAbort":
                if(input.StagingId is not null && Scalar("SELECT id FROM core_queue_sessions WHERE id=$p0 AND kind='staging'",input.StagingId) is not null)
                    RemoveSession(input.StagingId);
                break;
            case "insert":
            {
                var destination=input.StagingId??"active";
                if(input.StagingId is not null
                    && Scalar("SELECT id FROM core_queue_sessions WHERE id=$p0 AND kind='staging'",destination) is null)
                    throw new InvalidOperationException("Unknown queue replacement.");
                var items=input.Items??[];
                var ordinal=input.BeforeQueueId is {} anchor?Convert.ToInt64(Scalar("SELECT ordinal FROM core_queue_items WHERE session_id=$p0 AND queue_id=$p1",destination,anchor)
                    ??throw new InvalidOperationException("Queue insertion anchor is stale."))
                    :Convert.ToInt64(Scalar("SELECT COALESCE(MAX(ordinal)+1,0) FROM core_queue_items WHERE session_id=$p0",destination));
                Run("UPDATE core_queue_items SET ordinal=ordinal+$p0 WHERE session_id=$p1 AND ordinal>=$p2",items.Count,destination,ordinal);
                foreach(var item in items) { cancellation.ThrowIfCancellationRequested();InsertItem(destination,ordinal++,item,libraryPath); }
                if(destination=="active" && items.Any(item=>item.TryGetProperty("automaticShuffle",out var automatic)&&automatic.ValueKind==JsonValueKind.True))
                {
                    Run("DELETE FROM core_queue_items WHERE session_id='active' AND COALESCE(json_extract(item_json,'$.automaticShuffle'),0)=1 AND queue_id!=(SELECT COALESCE(current_queue_id,'') FROM core_queue_sessions WHERE id='active') AND queue_id NOT IN (SELECT queue_id FROM core_queue_items WHERE session_id='active' AND json_extract(item_json,'$.automaticShuffle')=1 ORDER BY ordinal DESC LIMIT 60)");
                    if(Convert.ToInt64(Scalar("SELECT changes()"))>0)Move([],null);
                }
                structure=destination=="active";break;
            }
            case "delete":
                Run("UPDATE core_queue_sessions SET history_json=(SELECT json_group_array(json(value)) FROM json_each(history_json) WHERE json_extract(value,'$.queueId') NOT IN (SELECT value FROM json_each($p0))) WHERE id='active'",JsonSerializer.Serialize(input.QueueIds??[]));
                Run("DELETE FROM core_queue_items WHERE session_id='active' AND queue_id IN (SELECT value FROM json_each($p0))",JsonSerializer.Serialize(input.QueueIds??[]));
                Move([],null);
                structure=true;break;
            case "clear":
                Run("DELETE FROM core_queue_items WHERE session_id='active'");Run("UPDATE core_queue_sessions SET history_json='[]' WHERE id='active'");structure=true;break;
            case "move":
                Move(input.QueueIds??[],input.BeforeQueueId);structure=true;break;
            case "patch":
                if(input.Patch is not {} patch || patch.GetRawText().Length>16384)throw new ArgumentException("Bounded track patch required.");
                if(patch.TryGetProperty("id",out var patchedId) && patchedId.GetString()!=input.TrackId)
                    throw new ArgumentException("Queue track identity cannot be patched.");
                Run("""
                    UPDATE core_queue_items SET track_json=json_patch(track_json,$p0),
                      title=COALESCE(json_extract($p0,'$.title'),title),artist=COALESCE(json_extract($p0,'$.artist'),artist),
                      album=COALESCE(json_extract($p0,'$.album'),album),duration_seconds=COALESCE(json_extract($p0,'$.duration'),duration_seconds)
                    WHERE session_id='active' AND track_id=$p1
                    """,patch.GetRawText(),input.TrackId);
                if(Convert.ToInt64(Scalar("SELECT COALESCE(MAX(length(CAST(track_json AS BLOB))),0) FROM core_queue_items WHERE session_id='active' AND track_id=$p0",input.TrackId))>65536)
                    throw new ArgumentException("Queue track recovery snapshot exceeds its byte limit.");
                metadata=true;
                structure=patch.TryGetProperty("path",out _) || patch.TryGetProperty("sampleRate",out _) || patch.TryGetProperty("unavailable",out _);
                break;
            case "save":
                if(input.Name is null || input.Name.Length is <1 or >200)throw new ArgumentException("Queue name required.");
                if(Convert.ToInt64(Scalar("SELECT COUNT(*) FROM core_queue_sessions WHERE kind='saved'")??0)>=12)throw new InvalidOperationException("Saved queue capacity reached.");
                staging="saved:"+Guid.NewGuid().ToString("N");CloneSession("active",staging,"saved",input.Name);break;
            case "restore":
                var restoredHistory=StoredText("SELECT history_json FROM core_queue_sessions WHERE id='active'")??"[]";
                if(input.StagingId is null || Scalar("SELECT id FROM core_queue_sessions WHERE id=$p0 AND kind='saved'",input.StagingId) is null)throw new InvalidOperationException("Saved queue does not exist.");
                CloneSession(input.StagingId,"active","active");
                PreservePlaybackHeader(before,restoredHistory);
                Run("UPDATE core_queue_sessions SET undo_token=$p0 WHERE id='active'",before["undoToken"]?.GetValue<string>());
                structure=true;break;
            case "rollback":
                ExportLegacy();
                Run("UPDATE core_queue_owner SET owner='legacy' WHERE id=1");
                break;
            default:
                structure=ExecuteAdditionalEdit(input)??throw new ArgumentException("Unknown queue operation.");
                if(input.Operation is "flags" or "selectionFlags" && input.Flags is {} changedFlags && !changedFlags.TryGetProperty("queuedNext",out _)) { structure=false;metadata=true; }
                break;
        }
        cancellation.ThrowIfCancellationRequested();
        if(structure && input.Operation is ("clear" or "replaceCommit" or "restore" or "temporaryEnd" or "undo"))ClearSelection();
        if(input.Operation=="undo")
        {
            Run("INSERT INTO core_queue_selection VALUES(1,$p0,0,0,1)","undo:"+input.UndoToken);
            Run("INSERT INTO core_queue_selection_items SELECT selected.queue_id FROM core_queue_undo_selection_items AS selected JOIN core_queue_items AS item ON item.session_id='active' AND item.queue_id=selected.queue_id");
            Run("DELETE FROM core_queue_undo_selection_items");
        }
        if(structure || mode || metadata)
        {
            Run("""
                UPDATE core_queue_sessions SET revision=$p0,structure_revision=$p1,
                  current_queue_id=CASE WHEN EXISTS(SELECT 1 FROM core_queue_items WHERE session_id='active' AND queue_id=current_queue_id) THEN current_queue_id ELSE NULL END,
                  updated_at=$p2 WHERE id='active'
                """,before["revision"]!.GetValue<long>()+1,before["structureRevision"]!.GetValue<long>()+(structure?1:0),DateTime.UtcNow.ToString("O"));
        }
        var result=new JsonObject{["committed"]=true,["summary"]=Summary()};
        if(staging is not null)result["stagingId"]=staging;if(undo is not null)result["undoToken"]=undo;
        Run("INSERT INTO core_queue_receipts VALUES($p0,$p1,$p2)",input.CommandId,result.ToJsonString(),DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Run("DELETE FROM core_queue_receipts WHERE command_id IN (SELECT command_id FROM core_queue_receipts ORDER BY created_at DESC,command_id LIMIT -1 OFFSET 128)");
        transaction.Commit();return result;
    }

    private void Move(List<string> ids,string? beforeQueueId,bool selection=false,bool ordered=false,bool front=false)
    {
        if(ids.Contains(beforeQueueId??"") || selection && beforeQueueId is not null && Scalar("SELECT queue_id FROM core_queue_selection_items WHERE queue_id=$p0",beforeQueueId) is not null)return;
        // Membership is only the bounded command's occurrence IDs. SQLite owns
        // ordering work; no complete queue or metadata array crosses the RPC.
        var json=JsonSerializer.Serialize(ids);
        var members=selection?"SELECT queue_id AS value FROM core_queue_selection_items WHERE $p0 IS NOT NULL":"SELECT value FROM json_each($p0)";
        var selected=Convert.ToInt64(Scalar($"SELECT COUNT(*) FROM core_queue_items WHERE session_id='active' AND queue_id IN ({members})",json));
        Run("CREATE TEMP TABLE IF NOT EXISTS queue_move_order(queue_id TEXT PRIMARY KEY,position INTEGER NOT NULL) WITHOUT ROWID");
        Run("DELETE FROM queue_move_order");
        Run($"""
            INSERT INTO queue_move_order
            SELECT queue_id,ROW_NUMBER() OVER(ORDER BY ordinal,queue_id)-1 FROM core_queue_items
            WHERE session_id='active' AND queue_id NOT IN ({members})
            """,json);
        var insertAt=front?0:beforeQueueId is null?Convert.ToInt64(Scalar("SELECT COUNT(*) FROM queue_move_order"))
            :Convert.ToInt64(Scalar("SELECT position FROM queue_move_order WHERE queue_id=$p0",beforeQueueId)
                ??throw new InvalidOperationException("Queue move anchor is stale."));
        Run("UPDATE queue_move_order SET position=position+$p0 WHERE position>=$p1",selected,insertAt);
        if(ordered)Run("""
            INSERT INTO queue_move_order SELECT item.queue_id,$p1+ROW_NUMBER() OVER(ORDER BY CAST(request.key AS INTEGER))-1
              FROM json_each($p0) AS request JOIN core_queue_items AS item ON item.session_id='active' AND item.queue_id=request.value
            """,json,insertAt);
        else Run($"""
            INSERT INTO queue_move_order SELECT queue_id,$p1+ROW_NUMBER() OVER(ORDER BY ordinal,queue_id)-1
            FROM core_queue_items WHERE session_id='active' AND queue_id IN ({members})
            """,json,insertAt);
        Run("UPDATE core_queue_items SET ordinal=(SELECT position FROM queue_move_order WHERE queue_move_order.queue_id=core_queue_items.queue_id) WHERE session_id='active'");
        Run("DELETE FROM queue_move_order");
    }
}
