const textOrder = `title_key COLLATE NOCASE, title_text COLLATE NOCASE,
  artist_key COLLATE NOCASE, artist_text COLLATE NOCASE, track_id`;
const refresh = (id: string, condition = '1') => `
UPDATE local_track_sort_keys SET
  playback_sort_count = MAX(
    COALESCE((SELECT play_count FROM tracks WHERE id = ${id}), 0),
    COALESCE((SELECT SUM(completed_count) FROM playback_history_stats WHERE track_id = ${id} AND media_type = 'local'), 0)),
  playback_sort_last = (SELECT MAX(last_started_at) FROM playback_history_stats WHERE track_id = ${id} AND media_type = 'local')
WHERE track_id = ${id} AND ${condition};
`;

export const trackPlaybackIndexSchemaSql = `
ALTER TABLE local_track_sort_keys ADD COLUMN playback_sort_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE local_track_sort_keys ADD COLUMN playback_sort_last TEXT;
CREATE INDEX idx_history_stats_track_local ON playback_history_stats(track_id, media_type, completed_count, last_started_at);
${refresh('local_track_sort_keys.track_id')}
CREATE INDEX idx_local_track_sort_play_count ON local_track_sort_keys(playback_sort_count DESC, playback_sort_last DESC, ${textOrder});
CREATE INDEX idx_local_track_sort_last_played ON local_track_sort_keys((playback_sort_last IS NULL), playback_sort_last DESC, ${textOrder});
CREATE TRIGGER local_track_playback_after_key_insert AFTER INSERT ON local_track_sort_keys BEGIN
  ${refresh('NEW.track_id')}
END;
CREATE TRIGGER local_track_playback_after_track_update AFTER UPDATE OF play_count ON tracks
WHEN OLD.play_count IS NOT NEW.play_count BEGIN
  ${refresh('NEW.id')}
END;
CREATE TRIGGER local_track_playback_after_history_insert AFTER INSERT ON playback_history_stats BEGIN
  ${refresh('NEW.track_id')}
END;
CREATE TRIGGER local_track_playback_after_history_delete AFTER DELETE ON playback_history_stats BEGIN
  ${refresh('OLD.track_id')}
END;
CREATE TRIGGER local_track_playback_after_history_update
AFTER UPDATE OF track_id, media_type, completed_count, last_started_at ON playback_history_stats
WHEN OLD.track_id IS NOT NEW.track_id OR OLD.media_type IS NOT NEW.media_type
  OR OLD.completed_count IS NOT NEW.completed_count OR OLD.last_started_at IS NOT NEW.last_started_at BEGIN
  ${refresh('OLD.track_id')}
  ${refresh('NEW.track_id', 'NEW.track_id IS NOT OLD.track_id')}
END;
INSERT OR REPLACE INTO library_maintenance_flags(key,value,updated_at)
  VALUES('local_track_query.playback','1',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
`;
