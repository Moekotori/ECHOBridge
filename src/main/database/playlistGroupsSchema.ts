/** Groups organize playlists only; deleting a group never deletes its playlists. */
export const playlistGroupsSchemaSql = `
  CREATE TABLE IF NOT EXISTS playlist_groups (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    collapsed INTEGER NOT NULL DEFAULT 0,
    position INTEGER NOT NULL DEFAULT 0
  );
  CREATE TABLE IF NOT EXISTS playlist_group_members (
    playlist_id TEXT PRIMARY KEY REFERENCES playlists(id) ON DELETE CASCADE,
    group_id TEXT NOT NULL REFERENCES playlist_groups(id) ON DELETE CASCADE
  );
  CREATE INDEX IF NOT EXISTS idx_playlist_group_members_group ON playlist_group_members(group_id);
`;
