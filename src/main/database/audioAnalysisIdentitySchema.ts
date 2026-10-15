export const audioAnalysisIdentitySql = `CREATE TABLE IF NOT EXISTS track_audio_identity (
  track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
  fingerprint TEXT NOT NULL, size_bytes INTEGER NOT NULL, mtime_ms REAL NOT NULL, ctime_ms REAL NOT NULL
);`;

