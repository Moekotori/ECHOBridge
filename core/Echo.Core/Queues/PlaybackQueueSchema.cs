namespace Echo.Core.Queues;

internal static class PlaybackQueueSchema
{
    public const string Sql = """
        PRAGMA journal_mode=WAL;
        PRAGMA busy_timeout=5000;
        PRAGMA cache_size=-2048;
        PRAGMA temp_store=FILE;
        CREATE TABLE IF NOT EXISTS core_queue_owner (
          id INTEGER PRIMARY KEY CHECK(id=1), owner TEXT NOT NULL CHECK(owner IN ('core','legacy')),
          application_token TEXT
        );
        CREATE TABLE IF NOT EXISTS core_queue_temporary (
          id INTEGER PRIMARY KEY CHECK(id=1), descriptor_json TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS core_queue_track_memory (
          session_id TEXT PRIMARY KEY, track_json TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS core_queue_sessions (
          id TEXT PRIMARY KEY, session_token TEXT NOT NULL, kind TEXT NOT NULL, name TEXT,
          revision INTEGER NOT NULL DEFAULT 0, structure_revision INTEGER NOT NULL DEFAULT 0,
          pointer_revision INTEGER NOT NULL DEFAULT 0, total INTEGER NOT NULL DEFAULT 0,
          duration_seconds REAL NOT NULL DEFAULT 0,
          current_queue_id TEXT, current_track_id TEXT, mode_json TEXT NOT NULL,
          resume_json TEXT, history_json TEXT NOT NULL DEFAULT '[]', undo_token TEXT,
          updated_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS core_queue_items (
          session_id TEXT NOT NULL, queue_id TEXT NOT NULL, ordinal INTEGER NOT NULL,
          track_id TEXT NOT NULL, library_path TEXT, item_json TEXT NOT NULL,
          track_json TEXT NOT NULL, title TEXT NOT NULL, artist TEXT NOT NULL,
          album TEXT NOT NULL, duration_seconds REAL NOT NULL,
          PRIMARY KEY(session_id,queue_id)
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS idx_core_queue_order ON core_queue_items(session_id,ordinal,queue_id);
        CREATE INDEX IF NOT EXISTS idx_core_queue_track ON core_queue_items(session_id,track_id,ordinal);
        CREATE TABLE IF NOT EXISTS core_queue_receipts (
          command_id TEXT PRIMARY KEY, result_json TEXT NOT NULL, created_at INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS core_queue_migration (
          id INTEGER PRIMARY KEY CHECK(id=1), source_version INTEGER NOT NULL,
          cursor INTEGER NOT NULL DEFAULT -1, completed INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS core_queue_selection(id INTEGER PRIMARY KEY CHECK(id=1),token TEXT NOT NULL,total INTEGER NOT NULL,flagged INTEGER NOT NULL,revision INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS core_queue_undo_selection_items(queue_id TEXT PRIMARY KEY) WITHOUT ROWID;
        CREATE TABLE IF NOT EXISTS core_queue_selection_items(queue_id TEXT PRIMARY KEY) WITHOUT ROWID;
        CREATE TRIGGER IF NOT EXISTS core_queue_selection_track_delete BEFORE DELETE ON core_queue_items WHEN old.session_id='active' BEGIN
          DELETE FROM core_queue_selection_items WHERE queue_id=old.queue_id;
          UPDATE core_queue_selection SET revision=revision+1 WHERE id=1;
        END;
        CREATE TRIGGER IF NOT EXISTS core_queue_selection_track_flags AFTER UPDATE OF item_json ON core_queue_items WHEN new.session_id='active' BEGIN
          UPDATE core_queue_selection SET flagged=flagged+COALESCE(json_extract(new.item_json,'$.removeAfterPlay'),0)-COALESCE(json_extract(old.item_json,'$.removeAfterPlay'),0)
            WHERE id=1 AND EXISTS(SELECT 1 FROM core_queue_selection_items WHERE queue_id=new.queue_id);
        END;
        CREATE TRIGGER IF NOT EXISTS core_queue_selection_insert AFTER INSERT ON core_queue_selection_items BEGIN
          UPDATE core_queue_selection SET total=total+1,flagged=flagged+COALESCE((SELECT json_extract(item_json,'$.removeAfterPlay') FROM core_queue_items WHERE session_id='active' AND queue_id=new.queue_id),0);
        END;
        CREATE TRIGGER IF NOT EXISTS core_queue_selection_delete AFTER DELETE ON core_queue_selection_items BEGIN
          UPDATE core_queue_selection SET total=total-1,flagged=flagged-COALESCE((SELECT json_extract(item_json,'$.removeAfterPlay') FROM core_queue_items WHERE session_id='active' AND queue_id=old.queue_id),0);
        END;
        CREATE TABLE IF NOT EXISTS core_queue_legacy_snapshot (
          id INTEGER PRIMARY KEY CHECK(id=1), library_path TEXT NOT NULL,
          payload_json TEXT NOT NULL, updated_at TEXT NOT NULL, resume_json TEXT
        );
        CREATE TRIGGER IF NOT EXISTS core_queue_items_insert AFTER INSERT ON core_queue_items BEGIN
          UPDATE core_queue_sessions SET total=total+1,duration_seconds=duration_seconds+new.duration_seconds
            WHERE id=new.session_id;
        END;
        CREATE TRIGGER IF NOT EXISTS core_queue_items_delete AFTER DELETE ON core_queue_items BEGIN
          UPDATE core_queue_sessions SET total=total-1,duration_seconds=duration_seconds-old.duration_seconds
            WHERE id=old.session_id;
        END;
        CREATE TRIGGER IF NOT EXISTS core_queue_items_update AFTER UPDATE OF duration_seconds ON core_queue_items BEGIN
          UPDATE core_queue_sessions SET duration_seconds=duration_seconds+new.duration_seconds-old.duration_seconds
            WHERE id=new.session_id;
        END;
        """;
}
