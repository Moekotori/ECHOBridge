// Album ids are preserved by the existing stable-id reconciliation. Deliberately no FK to albums: a grouping refresh
// deletes/rebuilds that derived table, but must preserve the user's decisions.
export const albumCurationSchemaSql = `
  CREATE TABLE IF NOT EXISTS saved_album_views (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    conditions_json TEXT NOT NULL,
    created_at TEXT NOT NULL
  );
  CREATE TABLE IF NOT EXISTS album_version_members (
    album_id TEXT PRIMARY KEY,
    group_id TEXT NOT NULL,
    is_primary INTEGER NOT NULL DEFAULT 0 CHECK (is_primary IN (0, 1))
  );
  CREATE INDEX IF NOT EXISTS idx_album_version_group ON album_version_members(group_id);
  CREATE UNIQUE INDEX IF NOT EXISTS idx_album_version_primary
    ON album_version_members(group_id) WHERE is_primary = 1;
`;
