// Playlist display snapshots have different text from live tracks/albums.
// Persist only their three sort fields, with pure SQL invalidation for every writer.
export const playlistSortKeySchemaSql = `
CREATE TABLE IF NOT EXISTS local_playlist_sort_key_revision (
  id INTEGER PRIMARY KEY CHECK(id=1), revision INTEGER NOT NULL DEFAULT 1
);
INSERT OR IGNORE INTO local_playlist_sort_key_revision VALUES(1,1);
CREATE TABLE IF NOT EXISTS local_playlist_sort_keys (
  item_id TEXT PRIMARY KEY,
  title_key TEXT NOT NULL, title_text TEXT,
  artist_key TEXT NOT NULL, artist_text TEXT,
  album_key TEXT NOT NULL, album_text TEXT,
  item_updated_at TEXT NOT NULL
);
CREATE VIEW IF NOT EXISTS playlist_sort_source AS
SELECT p.rowid AS rowid, p.id, '' AS updated_at,
  COALESCE(p.title_snapshot,t.title,a.title,'') AS title,
  COALESCE(p.artist_snapshot,t.artist,a.album_artist,'') AS artist,
  COALESCE(p.album_snapshot,t.album,a.title,'') AS album
FROM playlist_items p
LEFT JOIN tracks t ON t.id=p.media_id
LEFT JOIN albums a ON a.id=p.media_id;
CREATE TRIGGER IF NOT EXISTS playlist_sort_revision_playlist_items_insert
AFTER INSERT ON playlist_items
BEGIN
  UPDATE local_playlist_sort_key_revision SET revision=revision+1 WHERE id=1;
END;
CREATE TRIGGER IF NOT EXISTS playlist_sort_revision_playlist_items_delete
AFTER DELETE ON playlist_items
BEGIN
  UPDATE local_playlist_sort_key_revision SET revision=revision+1 WHERE id=1;
END;
CREATE TRIGGER IF NOT EXISTS playlist_sort_revision_playlist_items_update
AFTER UPDATE OF media_id,title_snapshot,artist_snapshot,album_snapshot ON playlist_items WHEN OLD.media_id IS NOT NEW.media_id OR OLD.title_snapshot IS NOT NEW.title_snapshot OR OLD.artist_snapshot IS NOT NEW.artist_snapshot OR OLD.album_snapshot IS NOT NEW.album_snapshot
BEGIN
  UPDATE local_playlist_sort_key_revision SET revision=revision+1 WHERE id=1;
END;
CREATE TRIGGER IF NOT EXISTS playlist_sort_revision_tracks_insert
AFTER INSERT ON tracks
BEGIN
  UPDATE local_playlist_sort_key_revision SET revision=revision+1 WHERE id=1;
END;
CREATE TRIGGER IF NOT EXISTS playlist_sort_revision_tracks_delete
AFTER DELETE ON tracks
BEGIN
  UPDATE local_playlist_sort_key_revision SET revision=revision+1 WHERE id=1;
END;
CREATE TRIGGER IF NOT EXISTS playlist_sort_revision_tracks_update
AFTER UPDATE OF title,artist,album ON tracks WHEN OLD.title IS NOT NEW.title OR OLD.artist IS NOT NEW.artist OR OLD.album IS NOT NEW.album
BEGIN
  UPDATE local_playlist_sort_key_revision SET revision=revision+1 WHERE id=1;
END;
CREATE TRIGGER IF NOT EXISTS playlist_sort_revision_albums_insert
AFTER INSERT ON albums
BEGIN
  UPDATE local_playlist_sort_key_revision SET revision=revision+1 WHERE id=1;
END;
CREATE TRIGGER IF NOT EXISTS playlist_sort_revision_albums_delete
AFTER DELETE ON albums
BEGIN
  UPDATE local_playlist_sort_key_revision SET revision=revision+1 WHERE id=1;
END;
CREATE TRIGGER IF NOT EXISTS playlist_sort_revision_albums_update
AFTER UPDATE OF title,album_artist ON albums WHEN OLD.title IS NOT NEW.title OR OLD.album_artist IS NOT NEW.album_artist
BEGIN
  UPDATE local_playlist_sort_key_revision SET revision=revision+1 WHERE id=1;
END;
`;

