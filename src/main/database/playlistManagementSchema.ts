/** Recovery is bounded by Core (20 operations, 100000 rows, 32 MiB, 30 days). */
export const playlistManagementSchemaSql = `
  CREATE TABLE IF NOT EXISTS playlist_organization (
    playlist_id TEXT PRIMARY KEY REFERENCES playlists(id) ON DELETE CASCADE,
    pinned INTEGER NOT NULL DEFAULT 0,
    position INTEGER NOT NULL DEFAULT 0
  );
  CREATE TABLE IF NOT EXISTS playlist_recovery (
    id TEXT PRIMARY KEY,
    playlist_id TEXT NOT NULL,
    name TEXT NOT NULL,
    action TEXT NOT NULL,
    created_at TEXT NOT NULL,
    playlist_json TEXT,
    rules_json TEXT,
    target_playlist_id TEXT
  );
  CREATE TABLE IF NOT EXISTS playlist_recovery_items (
    recovery_id TEXT NOT NULL REFERENCES playlist_recovery(id) ON DELETE CASCADE,
    item_id TEXT NOT NULL,
    payload TEXT NOT NULL,
    role TEXT NOT NULL DEFAULT 'source',
    PRIMARY KEY (recovery_id, item_id, role)
  );
  CREATE INDEX IF NOT EXISTS idx_playlist_recovery_created ON playlist_recovery(created_at);
`;
