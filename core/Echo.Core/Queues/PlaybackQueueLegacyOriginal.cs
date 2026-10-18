using System.Text.Json;

namespace Echo.Core.Queues;

public sealed partial class PlaybackQueueStorage
{
    private bool MigrateLegacyOriginal(string libraryPath,CancellationToken cancellation)
    {
        if(Convert.ToInt64(Scalar("SELECT COALESCE(json_extract(payload_json,'$.playlistPlayback.active'),0) FROM core_queue_legacy_snapshot WHERE id=1")??0)!=1
            || Scalar("SELECT json_extract(payload_json,'$.playlistPlayback.snapshot') FROM core_queue_legacy_snapshot WHERE id=1") is null)return false;
        if(Scalar("SELECT id FROM core_queue_sessions WHERE id='migration-original'") is null)
        {
            using var transaction=Begin();
            NewSession("migration-original","staging");
            Run("""
                UPDATE core_queue_sessions SET
                  current_queue_id=(SELECT json_extract(payload_json,'$.playlistPlayback.snapshot.currentQueueId') FROM core_queue_legacy_snapshot WHERE id=1),
                  current_track_id=(SELECT json_extract(payload_json,'$.playlistPlayback.snapshot.currentTrackId') FROM core_queue_legacy_snapshot WHERE id=1),
                  mode_json=COALESCE((SELECT json_extract(payload_json,'$.playlistPlayback.snapshot.mode') FROM core_queue_legacy_snapshot WHERE id=1),mode_json),
                  resume_json=(SELECT json_extract(payload_json,'$.playlistPlayback.snapshot.resume') FROM core_queue_legacy_snapshot WHERE id=1),
                  history_json=COALESCE((SELECT json_group_array(json(value)) FROM (
                    SELECT value FROM (SELECT key,value FROM core_queue_legacy_snapshot,json_each(payload_json,'$.playlistPlayback.snapshot.history')
                    WHERE core_queue_legacy_snapshot.id=1 ORDER BY CAST(key AS INTEGER) DESC LIMIT 500) ORDER BY CAST(key AS INTEGER))), '[]')
                WHERE id='migration-original'
                """);
            Run("INSERT INTO core_queue_track_memory SELECT 'migration-original',json_extract(payload_json,'$.playlistPlayback.snapshot.lastPlayedTrack') FROM core_queue_legacy_snapshot WHERE id=1 AND json_type(payload_json,'$.playlistPlayback.snapshot.lastPlayedTrack')='object'");
            transaction.Commit();
        }
        var cursor=Convert.ToInt64(Scalar("SELECT COALESCE(MAX(ordinal),-1) FROM core_queue_items WHERE session_id='migration-original'")??-1);
        using var command=Command("SELECT CAST(item.key AS INTEGER),item.value FROM core_queue_legacy_snapshot,json_each(payload_json,'$.playlistPlayback.snapshot.items') AS item WHERE core_queue_legacy_snapshot.id=1 AND CAST(item.key AS INTEGER)>$p0",cursor);
        using var reader=command.ExecuteReader();
        var rows=new List<(long Ordinal,string Json)>(128);
        void Commit()
        {
            using var transaction=Begin();
            foreach(var row in rows)
            {
                cancellation.ThrowIfCancellationRequested();
                using var json=JsonDocument.Parse(row.Json);InsertItem("migration-original",row.Ordinal,json.RootElement,libraryPath);
            }
            transaction.Commit();rows.Clear();
        }
        while(reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();rows.Add((reader.GetInt64(0),reader.GetString(1)));
            if(rows.Count==128)Commit();
        }
        if(rows.Count>0)Commit();
        return true;
    }
}
