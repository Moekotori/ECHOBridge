using System.Text.Json;
using Echo.Core.Protocol;

namespace Echo.Core.Queues;

public sealed partial class PlaybackQueueStorage
{
    private void ClearSelection() { Run("DELETE FROM core_queue_selection;DELETE FROM core_queue_selection_items"); }
    private bool? ExecuteSelection(PlaybackQueueCommand input)
    {
        if(!input.Operation.StartsWith("selection",StringComparison.Ordinal))return null;
        if(input.Operation=="selectionBegin")
        {
            if(input.StagingId is null || input.StagingId.Length is <1 or >128)throw new ArgumentException("Selection identity required.");
            ClearSelection();
            Run("INSERT INTO core_queue_selection VALUES(1,$p0,0,0,0)",input.StagingId);return false;
        }
        if(input.Operation=="selectionClear") { ClearSelection();return false; }
        if(input.StagingId is null || input.StagingId!=StoredText("SELECT token FROM core_queue_selection WHERE id=1"))throw new InvalidOperationException("Queue selection is stale.");
        switch(input.Operation)
        {
            case "selectionAdd": case "selectionToggle":
                if(input.Operation=="selectionAdd" || input.Selected)
                    Run("INSERT OR IGNORE INTO core_queue_selection_items SELECT queue_id FROM core_queue_items WHERE session_id='active' AND queue_id IN (SELECT value FROM json_each($p0))",JsonSerializer.Serialize(input.QueueIds??[]));
                else Run("DELETE FROM core_queue_selection_items WHERE queue_id IN (SELECT value FROM json_each($p0))",JsonSerializer.Serialize(input.QueueIds??[]));
                Run("UPDATE core_queue_selection SET revision=revision+1 WHERE id=1");return false;
            case "selectionDelete":
                Run("UPDATE core_queue_sessions SET history_json=(SELECT json_group_array(json(value)) FROM json_each(history_json) WHERE json_extract(value,'$.queueId') NOT IN (SELECT queue_id FROM core_queue_selection_items)) WHERE id='active'");
                Run("DELETE FROM core_queue_items WHERE session_id='active' AND queue_id IN (SELECT queue_id FROM core_queue_selection_items)");Move([],null);return true;
            case "selectionFlags":
                if(input.Flags is not {} flags || flags.ValueKind!=JsonValueKind.Object || flags.GetRawText().Length>256)throw new ArgumentException("Bounded queue flags required.");
                foreach(var property in flags.EnumerateObject())if(property.Name is not ("removeAfterPlay" or "queuedNext") || property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Invalid queue flag.");
                Run("UPDATE core_queue_items SET item_json=json_patch(item_json,$p0) WHERE session_id='active' AND queue_id IN (SELECT queue_id FROM core_queue_selection_items)",flags.GetRawText());
                Run("UPDATE core_queue_selection SET flagged=(SELECT COUNT(*) FROM core_queue_items WHERE session_id='active' AND json_extract(item_json,'$.removeAfterPlay')=1 AND queue_id IN (SELECT queue_id FROM core_queue_selection_items)),revision=revision+1 WHERE id=1");return true;
            case "selectionMove":
                var anchor=input.BeforeQueueId;
                if(input.CurrentQueueId is {} current)
                {
                    Run("DELETE FROM core_queue_selection_items WHERE queue_id=$p0",current);
                    anchor=StoredText("SELECT queue_id FROM core_queue_items WHERE session_id='active' AND ordinal>(SELECT ordinal FROM core_queue_items WHERE session_id='active' AND queue_id=$p0) AND queue_id NOT IN (SELECT queue_id FROM core_queue_selection_items) ORDER BY ordinal LIMIT 1",current);
                }
                if(anchor is not null && Scalar("SELECT queue_id FROM core_queue_selection_items WHERE queue_id=$p0",anchor) is not null)
                    anchor=StoredText("SELECT queue_id FROM core_queue_items WHERE session_id='active' AND ordinal>=(SELECT ordinal FROM core_queue_items WHERE session_id='active' AND queue_id=$p0) AND queue_id NOT IN (SELECT queue_id FROM core_queue_selection_items) ORDER BY ordinal LIMIT 1",anchor);
                Move([],anchor,true);return true;
            default:throw new ArgumentException("Unknown queue selection command.");
        }
    }
}
