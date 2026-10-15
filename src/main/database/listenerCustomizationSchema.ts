// User decisions survive derived-index rebuilds. SQL protection covers both scan writers.
const protectCuration = (event: string, name: string) => `
CREATE TRIGGER IF NOT EXISTS ${name} AFTER ${event} ON tracks
WHEN EXISTS (SELECT 1 FROM track_curation WHERE track_id = NEW.id AND (
  (json_extract(overrides_json, '$.artist') IS NOT NULL AND NEW.artist IS NOT json_extract(overrides_json, '$.artist')) OR
  (json_extract(overrides_json, '$.album') IS NOT NULL AND NEW.album IS NOT json_extract(overrides_json, '$.album')) OR
  (json_extract(overrides_json, '$.albumArtist') IS NOT NULL AND NEW.album_artist IS NOT json_extract(overrides_json, '$.albumArtist')) OR
  (json_extract(sources_json, '$.artist') IS NOT NULL AND json_extract(NEW.field_sources_json, '$.artist') IS NOT json_extract(sources_json, '$.artist')) OR
  (json_extract(sources_json, '$.album') IS NOT NULL AND json_extract(NEW.field_sources_json, '$.album') IS NOT json_extract(sources_json, '$.album')) OR
  (json_extract(sources_json, '$.albumArtist') IS NOT NULL AND json_extract(NEW.field_sources_json, '$.albumArtist') IS NOT json_extract(sources_json, '$.albumArtist'))))
BEGIN
  UPDATE tracks SET
    artist = COALESCE((SELECT json_extract(overrides_json, '$.artist') FROM track_curation WHERE track_id = NEW.id), NEW.artist),
    album = COALESCE((SELECT json_extract(overrides_json, '$.album') FROM track_curation WHERE track_id = NEW.id), NEW.album),
    album_artist = COALESCE((SELECT json_extract(overrides_json, '$.albumArtist') FROM track_curation WHERE track_id = NEW.id), NEW.album_artist),
    field_sources_json = json_patch(COALESCE(NEW.field_sources_json, '{}'), (SELECT sources_json FROM track_curation WHERE track_id = NEW.id)),
    search_terms = substr(NEW.search_terms, 1, 16384) || ' ' || (SELECT search_terms FROM track_curation WHERE track_id = NEW.id)
  WHERE id = NEW.id;
END;
`;
export const listenerCustomizationSchemaSql = `
CREATE TABLE IF NOT EXISTS track_curation (
  track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
  original_json TEXT NOT NULL,
  overrides_json TEXT NOT NULL,
  sources_json TEXT NOT NULL,
  search_terms TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS track_visual_presets (track_id TEXT PRIMARY KEY, preset_id TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS lyric_segmentation (track_id TEXT PRIMARY KEY, fingerprint TEXT NOT NULL, segments_json TEXT NOT NULL);
${protectCuration('INSERT', 'track_curation_after_insert')}
${protectCuration('UPDATE OF artist, album, album_artist, field_sources_json', 'track_curation_after_update')}
CREATE TRIGGER IF NOT EXISTS listener_customization_after_track_delete AFTER DELETE ON tracks BEGIN
  DELETE FROM track_visual_presets WHERE track_id = OLD.id;
  DELETE FROM lyric_segmentation WHERE track_id = OLD.id;
END;
`;
