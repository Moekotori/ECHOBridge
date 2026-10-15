export const personalLibrarySchemaSql = `
  CREATE TABLE IF NOT EXISTS personal_tags (
    id TEXT PRIMARY KEY, name TEXT NOT NULL, name_key TEXT NOT NULL UNIQUE
  );
  CREATE TABLE IF NOT EXISTS personal_tag_items (
    tag_id TEXT NOT NULL REFERENCES personal_tags(id) ON DELETE CASCADE,
    kind TEXT NOT NULL CHECK (kind IN ('track', 'album', 'artist')),
    target_id TEXT NOT NULL, title TEXT NOT NULL, subtitle TEXT NOT NULL,
    PRIMARY KEY (tag_id, kind, target_id)
  );
  CREATE INDEX IF NOT EXISTS idx_personal_tag_target ON personal_tag_items(kind, target_id, tag_id);
  CREATE INDEX IF NOT EXISTS idx_personal_tag_browse ON personal_tag_items(tag_id, kind, title COLLATE NOCASE, target_id);
  CREATE TABLE IF NOT EXISTS listen_later_albums (
    album_id TEXT PRIMARY KEY, title TEXT NOT NULL, artist TEXT NOT NULL,
    added_at TEXT NOT NULL, completed INTEGER NOT NULL DEFAULT 0 CHECK (completed IN (0, 1)),
    track_id TEXT, position_seconds REAL NOT NULL DEFAULT 0,
    track_finished INTEGER NOT NULL DEFAULT 0 CHECK (track_finished IN (0, 1))
  );
  CREATE INDEX IF NOT EXISTS idx_listen_later_browse ON listen_later_albums(completed, added_at DESC, album_id);
`;
