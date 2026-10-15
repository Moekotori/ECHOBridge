export const replayGainMeasurementSql = `
CREATE TABLE IF NOT EXISTS replay_gain_measurements (
  track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
  integrated_lufs REAL NOT NULL,
  peak REAL,
  histogram_json TEXT NOT NULL,
  version INTEGER NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS replay_gain_album_measurements (
  album_id TEXT PRIMARY KEY REFERENCES albums(id) ON DELETE CASCADE,
  integrated_lufs REAL NOT NULL,
  peak REAL,
  updated_at TEXT NOT NULL
);
CREATE TRIGGER IF NOT EXISTS replay_gain_measurement_deleted AFTER DELETE ON replay_gain_measurements
BEGIN
  DELETE FROM replay_gain_album_measurements WHERE album_id IN (SELECT album_id FROM album_tracks WHERE track_id = old.track_id);
END;
CREATE TRIGGER IF NOT EXISTS replay_gain_album_member_added AFTER INSERT ON album_tracks
BEGIN DELETE FROM replay_gain_album_measurements WHERE album_id = new.album_id; END;
CREATE TRIGGER IF NOT EXISTS replay_gain_album_member_removed AFTER DELETE ON album_tracks
BEGIN DELETE FROM replay_gain_album_measurements WHERE album_id = old.album_id; END;
CREATE TRIGGER IF NOT EXISTS replay_gain_track_availability_changed AFTER UPDATE OF missing ON tracks
WHEN old.missing IS NOT new.missing
BEGIN DELETE FROM replay_gain_album_measurements WHERE album_id IN (SELECT album_id FROM album_tracks WHERE track_id = new.id); END;
`;
