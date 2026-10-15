// Instrumental detection results are library-only facts: nothing is written back to media files.
// Analysis rows follow the audio identity (see AudioAnalysisIdentityStore); manual marks survive file changes.
export const vocalDetectionSchemaSql = `
  CREATE TABLE IF NOT EXISTS track_vocal_analysis (
    track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
    fingerprint TEXT NOT NULL,
    analyzer_version INTEGER NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('ok', 'error')),
    vocal_score REAL,
    chunk_count INTEGER NOT NULL DEFAULT 0,
    updated_at INTEGER NOT NULL
  );
  CREATE INDEX IF NOT EXISTS idx_track_vocal_analysis_version ON track_vocal_analysis(analyzer_version, status);
  CREATE TABLE IF NOT EXISTS track_vocal_overrides (
    track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
    verdict TEXT NOT NULL CHECK (verdict IN ('instrumental', 'vocal')),
    updated_at INTEGER NOT NULL
  );
`;
