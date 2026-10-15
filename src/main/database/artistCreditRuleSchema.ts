// User decisions about how artist credits are split and merged. Deliberately no foreign keys: the artist index
// (artists / artist_tracks) is rebuilt from scratch, but these rules must survive every rebuild, and the library
// grouping worker reads them straight from this table through its own connection.
export const artistCreditRuleSchemaSql = `
  CREATE TABLE IF NOT EXISTS artist_credit_rules (
    id TEXT PRIMARY KEY,
    kind TEXT NOT NULL CHECK (kind IN ('alias', 'keep', 'separators')),
    name TEXT NOT NULL,
    name_key TEXT NOT NULL,
    target TEXT,
    source TEXT NOT NULL DEFAULT 'user' CHECK (source IN ('user', 'tag')),
    suppressed INTEGER NOT NULL DEFAULT 0 CHECK (suppressed IN (0, 1)),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
  );
  CREATE UNIQUE INDEX IF NOT EXISTS idx_artist_credit_rules_identity ON artist_credit_rules(kind, name_key);
`;
