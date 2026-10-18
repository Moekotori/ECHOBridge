const visibleIndex = (name: string, columns: string) =>
  `CREATE INDEX idx_track_order_${name} ON tracks(${columns}) WHERE missing = 0 AND library_hidden = 0;`;

// Narrow numeric indexes keep write/storage cost bounded. SQLite orders ties
// with the unchanged full ORDER BY, rather than copying all text into every index.
export const trackOrderIndexSchemaSql = `
CREATE INDEX idx_local_track_sort_artist ON local_track_sort_keys(artist_key COLLATE NOCASE, artist_text COLLATE NOCASE);
CREATE INDEX idx_local_track_sort_album ON local_track_sort_keys(album_key COLLATE NOCASE, album_text COLLATE NOCASE);
${visibleIndex('duration', 'duration')}
${visibleIndex('year_asc', '(year IS NULL), year ASC')}
${visibleIndex('year_desc', '(year IS NULL), year DESC')}
${visibleIndex('audio_asc', '(sample_rate IS NULL OR sample_rate <= 0), sample_rate ASC, (bit_depth IS NULL OR bit_depth <= 0), bit_depth ASC')}
${visibleIndex('audio_desc', '(sample_rate IS NULL OR sample_rate <= 0), sample_rate DESC, (bit_depth IS NULL OR bit_depth <= 0), bit_depth DESC')}
${visibleIndex('created', 'created_at')}
${visibleIndex('updated', 'updated_at')}
${visibleIndex('modified', 'mtime_ms')}
${visibleIndex('bpm_asc', '(bpm IS NULL OR bpm <= 0), bpm ASC')}
${visibleIndex('bpm_desc', '(bpm IS NULL OR bpm <= 0), bpm DESC')}
${visibleIndex('bitrate', 'COALESCE(bitrate, 0)')}
${visibleIndex('quality_asc', 'COALESCE(bitrate, 0) ASC, size_bytes ASC')}
${visibleIndex('quality_desc', 'COALESCE(bitrate, 0) DESC, size_bytes DESC')}
INSERT OR REPLACE INTO library_maintenance_flags(key,value,updated_at)
  VALUES('local_track_query.order','1',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
`;
