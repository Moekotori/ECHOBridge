// Only references and time marks; no media, covers or PCM are copied.
export const savedSegmentsSchemaSql = `
  CREATE TABLE IF NOT EXISTS saved_segments (
    id TEXT PRIMARY KEY,
    track_id TEXT NOT NULL,
    name TEXT NOT NULL,
    start_seconds REAL NOT NULL CHECK (start_seconds >= 0),
    end_seconds REAL NOT NULL CHECK (end_seconds - start_seconds >= 0.5),
    created_at TEXT NOT NULL,
    title TEXT NOT NULL,
    artist TEXT NOT NULL,
    UNIQUE (track_id, start_seconds, end_seconds)
  );
  CREATE INDEX IF NOT EXISTS idx_saved_segments_created ON saved_segments(created_at DESC, id);
`;
