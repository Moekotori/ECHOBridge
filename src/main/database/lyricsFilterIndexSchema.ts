// Keep cache selection shared with LyricsService. JSON is inspected only when
// a cache entry changes or the derived index is first backfilled.
export const lyricsCachePrioritySql = `CASE
  WHEN provider = 'manual' OR acceptance_origin = 'manual' THEN 0
  WHEN provider = 'local' THEN 1
  WHEN provider = 'lrclib' THEN 2
  ELSE 3
END`;

const lyricsStateSql = `CASE
  WHEN kind NOT IN ('plain', 'synced') THEN 0
  WHEN EXISTS (
    SELECT 1 FROM json_each(lines_json) AS line
    WHERE json_array_length(line.value, '$.words') > 0
  ) THEN 2
  ELSE 1
END`;

const refreshTrackSql = (trackId: string, condition = '1') => `
  DELETE FROM track_lyrics_filter WHERE track_id = ${trackId} AND ${condition};
  INSERT INTO track_lyrics_filter(track_id, state)
  SELECT track_id, ${lyricsStateSql} FROM lyrics_cache
  WHERE track_id = ${trackId} AND ${condition}
  ORDER BY ${lyricsCachePrioritySql}, updated_at DESC LIMIT 1;
`;

export const lyricsFilterIndexSchemaSql = `
CREATE TABLE IF NOT EXISTS track_lyrics_filter (
  track_id TEXT NOT NULL PRIMARY KEY,
  state INTEGER NOT NULL CHECK (state IN (0, 1, 2))
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS idx_lyrics_cache_filter_priority
  ON lyrics_cache(track_id, ${lyricsCachePrioritySql}, updated_at DESC);
CREATE TRIGGER IF NOT EXISTS lyrics_filter_after_insert
AFTER INSERT ON lyrics_cache WHEN NEW.track_id IS NOT NULL BEGIN
  ${refreshTrackSql('NEW.track_id')}
END;
CREATE TRIGGER IF NOT EXISTS lyrics_filter_after_delete
AFTER DELETE ON lyrics_cache WHEN OLD.track_id IS NOT NULL BEGIN
  ${refreshTrackSql('OLD.track_id')}
END;
CREATE TRIGGER IF NOT EXISTS lyrics_filter_after_update
AFTER UPDATE OF track_id, provider, acceptance_origin, updated_at, kind, lines_json ON lyrics_cache
WHEN OLD.track_id IS NOT NEW.track_id OR OLD.provider IS NOT NEW.provider
  OR OLD.acceptance_origin IS NOT NEW.acceptance_origin OR OLD.updated_at IS NOT NEW.updated_at
  OR OLD.kind IS NOT NEW.kind OR OLD.lines_json IS NOT NEW.lines_json
BEGIN
  ${refreshTrackSql('OLD.track_id')}
  ${refreshTrackSql('NEW.track_id', 'NEW.track_id IS NOT OLD.track_id')}
END;
`;

export const lyricsFilterIndexMigrationSql = `${lyricsFilterIndexSchemaSql}
INSERT OR REPLACE INTO track_lyrics_filter(track_id, state)
SELECT source.track_id, (
  SELECT ${lyricsStateSql} FROM lyrics_cache
  WHERE track_id = source.track_id
  ORDER BY ${lyricsCachePrioritySql}, updated_at DESC LIMIT 1
)
FROM (SELECT DISTINCT track_id FROM lyrics_cache WHERE track_id IS NOT NULL) AS source;
`;
