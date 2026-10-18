using System.Text.Json.Nodes;

namespace Echo.Core.Queues;

public sealed partial class PlaybackQueueStorage
{
    private void PreservePlaybackHeader(JsonObject before,string history)
    {
        Run("""
            UPDATE core_queue_sessions SET
              current_queue_id=COALESCE((SELECT queue_id FROM core_queue_items WHERE session_id='active' AND queue_id=$p0),
                (SELECT queue_id FROM core_queue_items WHERE session_id='active' AND track_id=$p1 ORDER BY ordinal LIMIT 1)),
              current_track_id=$p1,resume_json=$p2,mode_json=$p3,pointer_revision=$p4,history_json=$p5 WHERE id='active'
            """,before["currentQueueId"]?.GetValue<string>(),before["currentTrackId"]?.GetValue<string>(),
            before["resume"]?.ToJsonString(),before["mode"]!.ToJsonString(),before["pointerRevision"]!.GetValue<long>(),history);
        Run("DELETE FROM core_queue_track_memory WHERE session_id='active'");
        if(before["lastPlayedTrack"] is {} track)Run("INSERT INTO core_queue_track_memory VALUES('active',$p0)",track.ToJsonString());
    }
}
