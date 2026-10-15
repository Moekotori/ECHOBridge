/** Small per-track preferences; imported media stays in the managed wallpaper directory. */
export const lyricsVideoBindingSchemaSql = `
  CREATE TABLE IF NOT EXISTS track_lyrics_video_backgrounds (
    track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
    video_path TEXT NOT NULL,
    start_seconds REAL NOT NULL DEFAULT 0,
    brightness_percent INTEGER NOT NULL DEFAULT 100,
    scale_percent INTEGER NOT NULL DEFAULT 100,
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    sync_enabled INTEGER NOT NULL DEFAULT 0
  );
`;
