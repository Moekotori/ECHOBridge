using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Queues;

public sealed partial class PlaybackQueueStorage
{
    private void NewSession(string id,string kind,string? name=null,string? mode=null)
    {
        Run("""
            INSERT INTO core_queue_sessions(id,session_token,kind,name,mode_json,updated_at)
            VALUES($p0,$p1,$p2,$p3,$p4,$p5)
            """,id,Guid.NewGuid().ToString("N"),kind,name,mode??DefaultMode,DateTime.UtcNow.ToString("O"));
    }
    private void CloneSession(string from,string to,string kind,string? name=null)
    {
        RemoveSession(to);
        Run("""
            INSERT INTO core_queue_sessions(id,session_token,kind,name,revision,structure_revision,pointer_revision,
              current_queue_id,current_track_id,mode_json,resume_json,history_json,updated_at)
            SELECT $p1,session_token,$p2,$p3,revision,structure_revision,pointer_revision,
              current_queue_id,current_track_id,mode_json,resume_json,history_json,updated_at
            FROM core_queue_sessions WHERE id=$p0
            """,from,to,kind,name);
        CopyItems(from,to);
    }
    private void MigrateLegacy(string libraryPath,CancellationToken cancellation)
    {
        if(Convert.ToInt64(Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='playback_queue_session'")??0)>0)
        {
            using var columns=Command("PRAGMA table_info(playback_queue_session)");using var reader=columns.ExecuteReader();
            var hasResume=false;while(reader.Read())if(reader.GetString(1)=="resume_json")hasResume=true;
            reader.Close();if(!hasResume)Run("ALTER TABLE playback_queue_session ADD COLUMN resume_json TEXT");
        }
        if(Convert.ToInt64(Scalar("SELECT completed FROM core_queue_migration WHERE id=1")??0)==1)return;
        if(Scalar("SELECT id FROM core_queue_migration WHERE id=1") is not null)
        {
            using var transaction=Begin();
            EnsureLegacySnapshotUnchanged(transaction);
            transaction.Commit();
        }
        if(Convert.ToInt64(Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='playback_queue_session'")??0)==0
            || Convert.ToInt64(Scalar("SELECT COUNT(*) FROM playback_queue_session WHERE id='active' AND version=1")??0)==0)
        {
            using var transaction=Begin();
            if(Scalar("SELECT id FROM core_queue_sessions WHERE id='active'") is null)NewSession("active","active");
            Run("INSERT OR REPLACE INTO core_queue_migration VALUES(1,1,-1,1)");transaction.Commit();return;
        }
        if(Scalar("SELECT id FROM core_queue_migration WHERE id=1") is null)
        {
            using var transaction=Begin();
            Run("""
                INSERT INTO core_queue_legacy_snapshot
                SELECT 1,$p0,payload_json,updated_at,resume_json FROM playback_queue_session WHERE id='active'
                """,libraryPath);
            NewSession("migration","staging");
            Run("""
                UPDATE core_queue_sessions SET
                  current_queue_id=(SELECT json_extract(payload_json,'$.currentQueueId') FROM core_queue_legacy_snapshot WHERE id=1),
                  current_track_id=(SELECT json_extract(payload_json,'$.currentTrackId') FROM core_queue_legacy_snapshot WHERE id=1),
                  mode_json=COALESCE((SELECT json_extract(payload_json,'$.mode') FROM core_queue_legacy_snapshot WHERE id=1),mode_json),
                  resume_json=(SELECT COALESCE(resume_json,json_extract(payload_json,'$.resume')) FROM core_queue_legacy_snapshot WHERE id=1),
                  history_json=COALESCE((SELECT json_group_array(json(value)) FROM (
                    SELECT value FROM (SELECT key,value FROM core_queue_legacy_snapshot,json_each(payload_json,'$.history')
                    WHERE core_queue_legacy_snapshot.id=1 ORDER BY CAST(key AS INTEGER) DESC LIMIT 500)
                    ORDER BY CAST(key AS INTEGER))), '[]')
                WHERE id='migration'
                """);
            Run("INSERT INTO core_queue_migration VALUES(1,1,-1,0)");transaction.Commit();
        }
        using(var transaction=Begin())
        {
            EnsureLegacySnapshotUnchanged(transaction);
            transaction.Commit();
        }
        // The immutable on-disk source is retained only until migration commits.
        // Restarting under a different library must not relabel earlier rows.
        var sourceLibraryPath=Scalar("SELECT library_path FROM core_queue_legacy_snapshot WHERE id=1") as string
            ??throw new InvalidOperationException("Queue migration source snapshot missing.");
        var cursor=Convert.ToInt64(Scalar("SELECT cursor FROM core_queue_migration WHERE id=1")??-1);
        // One streaming JSON reader only during the one-time V1 migration. Each
        // committed batch is bounded; cancellation/restart resumes the staging
        // session. The original V1 row is never changed by migration.
        using(var command=Command("""
            SELECT CAST(item.key AS INTEGER),item.value FROM core_queue_legacy_snapshot AS session,
              json_each(session.payload_json,'$.items') AS item
            WHERE session.id=1 AND CAST(item.key AS INTEGER)>$p0
            """,cursor))
        using(var reader=command.ExecuteReader())
        {
            var rows=new List<(long Ordinal,string Json)>(128);
            void CommitBatch()
            {
                using var transaction=Begin();
                foreach(var row in rows)
                {
                    cancellation.ThrowIfCancellationRequested();
                    using var item=JsonDocument.Parse(row.Json);InsertItem("migration",row.Ordinal,item.RootElement,sourceLibraryPath);
                }
                cursor=rows[^1].Ordinal;
                Run("UPDATE core_queue_migration SET cursor=$p0 WHERE id=1",cursor);transaction.Commit();rows.Clear();
            }
            while(reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();rows.Add((reader.GetInt64(0),reader.GetString(1)));
                if(rows.Count==128)CommitBatch();
            }
            if(rows.Count>0)CommitBatch();
        }
        var hasOriginal=MigrateLegacyOriginal(sourceLibraryPath,cancellation);
        using(var transaction=Begin())
        {
            EnsureLegacySnapshotUnchanged(transaction);
            var expected=Convert.ToInt64(Scalar("SELECT json_array_length(payload_json,'$.items') FROM core_queue_legacy_snapshot WHERE id=1")??0);
            var actual=Convert.ToInt64(Scalar("SELECT total FROM core_queue_sessions WHERE id='migration'")??0);
            if(expected!=actual)throw new InvalidOperationException("Queue migration occurrence count mismatch.");
            if(hasOriginal)
            {
                var originalCount=Convert.ToInt64(Scalar("SELECT json_array_length(payload_json,'$.playlistPlayback.snapshot.items') FROM core_queue_legacy_snapshot WHERE id=1")??0);
                if(originalCount!=Convert.ToInt64(Scalar("SELECT total FROM core_queue_sessions WHERE id='migration-original'")??0))throw new InvalidOperationException("Original queue migration count mismatch.");
                CloneSession("migration-original","original","original");RemoveSession("migration-original");
                Run("INSERT OR REPLACE INTO core_queue_temporary SELECT 1,json_object('label',json_extract(payload_json,'$.playlistPlayback.label'),'playlistId',json_extract(payload_json,'$.playlistPlayback.playlistId'),'windowSource',json(json_extract(payload_json,'$.playlistPlayback.windowSource'))) FROM core_queue_legacy_snapshot WHERE id=1");
            }
            CloneSession("migration","active","active");RemoveSession("migration");
            Run("INSERT INTO core_queue_track_memory SELECT 'active',json_extract(payload_json,'$.lastPlayedTrack') FROM core_queue_legacy_snapshot WHERE id=1 AND json_type(payload_json,'$.lastPlayedTrack')='object' ON CONFLICT(session_id) DO NOTHING");
            Run("DELETE FROM core_queue_legacy_snapshot");
            Run("UPDATE core_queue_migration SET completed=1 WHERE id=1");transaction.Commit();
        }
    }

    private void EnsureLegacySnapshotUnchanged(QueueTransaction transaction)
    {
        if(Convert.ToInt64(Scalar("""
            SELECT COUNT(*) FROM playback_queue_session AS original,core_queue_legacy_snapshot AS snapshot
            WHERE original.id='active' AND snapshot.id=1 AND original.version=1
              AND original.payload_json=snapshot.payload_json AND original.updated_at=snapshot.updated_at
              AND original.resume_json IS snapshot.resume_json
            """)??0)==1)return;
        RemoveSession("migration");
        RemoveSession("migration-original");
        Run("DELETE FROM core_queue_migration");
        Run("DELETE FROM core_queue_legacy_snapshot");
        transaction.Commit();
        throw new InvalidOperationException("Legacy queue changed during migration; partial import discarded.");
    }

    private void ExportLegacy()
    {
        Run("""
            CREATE TABLE IF NOT EXISTS playback_queue_session (
              id TEXT PRIMARY KEY CHECK(id='active'),version INTEGER NOT NULL,payload_json TEXT NOT NULL,
              updated_at TEXT NOT NULL,resume_json TEXT)
            """);
        // Explicit rollback only. Both active and original queues are projected
        // by SQLite; no complete object array crosses Main or renderer.
        Run("""
            CREATE TEMP VIEW IF NOT EXISTS core_queue_export_json AS
            SELECT id,updated_at,resume_json,json_object('version',1,'revision',revision,'currentQueueId',current_queue_id,
              'currentTrackId',current_track_id,'mode',json(mode_json),'resume',json(resume_json),
              'history',json(history_json),'updatedAt',updated_at,
              'playlistPlayback',NULL,
              'lastPlayedTrack',json(COALESCE((SELECT track_json FROM core_queue_track_memory WHERE session_id=session.id),'null')),
              'items',json((SELECT COALESCE(json_group_array(json(item)),'[]') FROM (
                SELECT json_set(item_json,'$.track',json(track_json)) AS item FROM core_queue_items
                WHERE session_id=session.id ORDER BY ordinal,queue_id)))) AS payload_json
            FROM core_queue_sessions AS session;
            INSERT INTO playback_queue_session(id,version,payload_json,updated_at,resume_json)
            SELECT 'active',1,json_set(active.payload_json,'$.playlistPlayback',json(CASE WHEN temporary.id=1
                THEN json_set(temporary.descriptor_json,'$.active',json('true'),'$.snapshot',json(original.payload_json)) ELSE 'null' END)),
                active.updated_at,active.resume_json
            FROM core_queue_export_json AS active
              LEFT JOIN core_queue_export_json AS original ON original.id='original'
              LEFT JOIN core_queue_temporary AS temporary ON temporary.id=1
            WHERE active.id='active'
            ON CONFLICT(id) DO UPDATE SET version=1,payload_json=excluded.payload_json,
              updated_at=excluded.updated_at,resume_json=excluded.resume_json
            """);
    }
}
