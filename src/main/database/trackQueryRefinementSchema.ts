const textOrder = 'title_key COLLATE NOCASE, title_text COLLATE NOCASE, artist_key COLLATE NOCASE, artist_text COLLATE NOCASE, track_id';
const fields = ['codec', 'disc_no', 'track_no', 'sample_rate', 'bit_depth'] as const;
const refresh = (id: string) => `UPDATE local_track_sort_keys SET
  ${fields.map(field => `order_${field} = (SELECT ${field} FROM tracks WHERE id = ${id})`).join(',\n  ')}
  WHERE track_id = ${id};`;

// Small metadata values share the existing sort-key owner. No second text-key
// copy or renderer cache; updates touch only the affected track and its indexes.
export const trackQueryRefinementSchemaSql = `
${fields.map(field => `ALTER TABLE local_track_sort_keys ADD COLUMN order_${field} ${field === 'codec' ? 'TEXT' : 'INTEGER'};`).join('\n')}
${refresh('local_track_sort_keys.track_id')}
CREATE INDEX idx_local_track_sort_play_count_asc ON local_track_sort_keys(playback_sort_count ASC, playback_sort_last DESC, ${textOrder});
CREATE INDEX idx_local_track_sort_codec_asc ON local_track_sort_keys(COALESCE(order_codec, '') COLLATE NOCASE ASC, ${textOrder});
CREATE INDEX idx_local_track_sort_codec_desc ON local_track_sort_keys(COALESCE(order_codec, '') COLLATE NOCASE DESC, ${textOrder});
CREATE INDEX idx_local_track_sort_track_number ON local_track_sort_keys((order_track_no IS NULL), COALESCE(order_disc_no, 1), COALESCE(order_track_no, 0), title_key COLLATE NOCASE);
CREATE INDEX idx_local_track_sort_audio_asc ON local_track_sort_keys((order_sample_rate IS NULL OR order_sample_rate <= 0), order_sample_rate ASC, (order_bit_depth IS NULL OR order_bit_depth <= 0), order_bit_depth ASC, ${textOrder});
CREATE INDEX idx_local_track_sort_audio_desc ON local_track_sort_keys((order_sample_rate IS NULL OR order_sample_rate <= 0), order_sample_rate DESC, (order_bit_depth IS NULL OR order_bit_depth <= 0), order_bit_depth DESC, ${textOrder});
CREATE INDEX idx_track_entity_facts ON tracks(id, play_count, last_played_at, created_at, mtime_ms) WHERE missing = 0 AND library_hidden = 0;
CREATE TRIGGER local_track_order_after_key_insert AFTER INSERT ON local_track_sort_keys BEGIN
  ${refresh('NEW.track_id')}
END;
CREATE TRIGGER local_track_order_after_track_update AFTER UPDATE OF ${fields.join(', ')} ON tracks
WHEN ${fields.map(field => `OLD.${field} IS NOT NEW.${field}`).join(' OR ')} BEGIN
  ${refresh('NEW.id')}
END;
INSERT OR REPLACE INTO library_maintenance_flags(key,value,updated_at)
  VALUES('local_track_query.refined','1',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
`;
