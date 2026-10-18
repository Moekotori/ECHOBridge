import { trackExcludeIndexUpdateRepairSql } from './trackExcludeIndexSchema';

export const trackQueryWriteMaintenanceSchemaSql = `
${trackExcludeIndexUpdateRepairSql}
DROP TRIGGER local_track_playback_after_key_insert;
DROP TRIGGER local_track_order_after_key_insert;
CREATE TRIGGER local_track_order_after_key_insert AFTER INSERT ON local_track_sort_keys BEGIN
  UPDATE local_track_sort_keys SET
    (order_codec, order_disc_no, order_track_no, order_sample_rate, order_bit_depth,
      playback_sort_count, playback_sort_last) = (
      SELECT codec, disc_no, track_no, sample_rate, bit_depth,
        MAX(COALESCE(play_count, 0), COALESCE((
          SELECT SUM(completed_count) FROM playback_history_stats
          WHERE track_id = NEW.track_id AND media_type = 'local'), 0)),
        (SELECT MAX(last_started_at) FROM playback_history_stats
          WHERE track_id = NEW.track_id AND media_type = 'local')
      FROM tracks WHERE id = NEW.track_id
    )
  WHERE track_id = NEW.track_id;
END;
DROP TRIGGER local_track_order_after_track_update;
CREATE TRIGGER local_track_order_after_track_update
AFTER UPDATE OF codec, disc_no, track_no, sample_rate, bit_depth ON tracks
WHEN OLD.codec IS NOT NEW.codec OR OLD.disc_no IS NOT NEW.disc_no OR OLD.track_no IS NOT NEW.track_no
  OR OLD.sample_rate IS NOT NEW.sample_rate OR OLD.bit_depth IS NOT NEW.bit_depth BEGIN
  UPDATE local_track_sort_keys SET order_codec = NEW.codec, order_disc_no = NEW.disc_no,
    order_track_no = NEW.track_no, order_sample_rate = NEW.sample_rate, order_bit_depth = NEW.bit_depth
  WHERE track_id = NEW.id;
END;
`;
