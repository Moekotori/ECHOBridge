const textFields = ['title_key', 'title_text', 'artist_key', 'artist_text', 'album_key', 'album_text', 'track_updated_at'];
const countFor = (id: string) => `MAX(COALESCE((SELECT play_count FROM tracks WHERE id = ${id}), 0),
  COALESCE((SELECT SUM(completed_count) FROM playback_history_stats WHERE track_id = ${id} AND media_type = 'local'), 0))`;
const lastFor = (id: string) => `(SELECT MAX(last_started_at) FROM playback_history_stats WHERE track_id = ${id} AND media_type = 'local')`;
const refreshPlayback = (id: string, condition = '1') => `
  UPDATE local_track_sort_keys SET playback_sort_count = ${countFor(id)}
  WHERE track_id = ${id} AND ${condition} AND playback_sort_count IS NOT ${countFor(id)};
  UPDATE local_track_sort_keys SET playback_sort_last = ${lastFor(id)}
  WHERE track_id = ${id} AND ${condition} AND playback_sort_last IS NOT ${lastFor(id)};
`;

// Both production sort-key writers use this view. The table receives complete
// rows, so its seven ordering indexes need no second initialization update.
export const trackQueryWriteOptimizationSchemaSql = `
DROP TRIGGER local_track_order_after_key_insert;
CREATE VIEW local_track_sort_key_writes AS SELECT track_id, ${textFields.join(', ')} FROM local_track_sort_keys;
CREATE TRIGGER local_track_sort_key_write INSTEAD OF INSERT ON local_track_sort_key_writes BEGIN
  INSERT INTO local_track_sort_keys(track_id, ${textFields.join(', ')},
    order_codec, order_disc_no, order_track_no, order_sample_rate, order_bit_depth, playback_sort_count, playback_sort_last)
  SELECT NEW.track_id, ${textFields.map(field => `NEW.${field}`).join(', ')},
    codec, disc_no, track_no, sample_rate, bit_depth,
    MAX(COALESCE(play_count, 0), COALESCE((SELECT SUM(completed_count) FROM playback_history_stats
      WHERE track_id = NEW.track_id AND media_type = 'local'), 0)),
    (SELECT MAX(last_started_at) FROM playback_history_stats WHERE track_id = NEW.track_id AND media_type = 'local')
  FROM tracks WHERE id = NEW.track_id
  ON CONFLICT(track_id) DO UPDATE SET ${textFields.map(field => `${field} = excluded.${field}`).join(', ')};
END;
DROP TRIGGER local_track_order_after_track_update;
CREATE TRIGGER local_track_order_after_codec_update AFTER UPDATE OF codec ON tracks
WHEN OLD.codec IS NOT NEW.codec BEGIN
  UPDATE local_track_sort_keys SET order_codec = NEW.codec WHERE track_id = NEW.id;
END;
CREATE TRIGGER local_track_order_after_number_update AFTER UPDATE OF disc_no, track_no ON tracks
WHEN OLD.disc_no IS NOT NEW.disc_no OR OLD.track_no IS NOT NEW.track_no BEGIN
  UPDATE local_track_sort_keys SET order_disc_no = NEW.disc_no, order_track_no = NEW.track_no WHERE track_id = NEW.id;
END;
CREATE TRIGGER local_track_order_after_audio_update AFTER UPDATE OF sample_rate, bit_depth ON tracks
WHEN OLD.sample_rate IS NOT NEW.sample_rate OR OLD.bit_depth IS NOT NEW.bit_depth BEGIN
  UPDATE local_track_sort_keys SET order_sample_rate = NEW.sample_rate, order_bit_depth = NEW.bit_depth WHERE track_id = NEW.id;
END;
DROP TRIGGER local_track_playback_after_track_update;
CREATE TRIGGER local_track_playback_after_track_update AFTER UPDATE OF play_count ON tracks
WHEN OLD.play_count IS NOT NEW.play_count BEGIN
  ${refreshPlayback('NEW.id')}
END;
DROP TRIGGER local_track_playback_after_history_insert;
CREATE TRIGGER local_track_playback_after_history_insert AFTER INSERT ON playback_history_stats BEGIN
  ${refreshPlayback('NEW.track_id')}
END;
DROP TRIGGER local_track_playback_after_history_delete;
CREATE TRIGGER local_track_playback_after_history_delete AFTER DELETE ON playback_history_stats BEGIN
  ${refreshPlayback('OLD.track_id')}
END;
DROP TRIGGER local_track_playback_after_history_update;
CREATE TRIGGER local_track_playback_after_history_update
AFTER UPDATE OF track_id, media_type, completed_count, last_started_at ON playback_history_stats
WHEN OLD.track_id IS NOT NEW.track_id OR OLD.media_type IS NOT NEW.media_type
  OR OLD.completed_count IS NOT NEW.completed_count OR OLD.last_started_at IS NOT NEW.last_started_at BEGIN
  ${refreshPlayback('OLD.track_id')}
  ${refreshPlayback('NEW.track_id', 'NEW.track_id IS NOT OLD.track_id')}
END;
`;
