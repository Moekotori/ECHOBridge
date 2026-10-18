const fields = ['title', 'artist', 'album', 'album_artist', 'genre', 'path'] as const;
const source = (field: string, prefix = '') => field === 'genre' ? `COALESCE(${prefix}genre, '')` : `${prefix}${field}`;
// This is exactly the existing SQLite exclusion normalization, including NULLs
// and SQLite's ASCII-only LOWER. Keep fields separate to preserve word boundaries.
const folded = (value: string) => `(' ' || LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(${value}, '-', ' '), '_', ' '), '.', ' '), '/', ' '), CHAR(92), ' '), ',', ' ')) || ' ')`;
const put = (prefix: string) => `INSERT OR REPLACE INTO local_track_exclude_fields(track_id, ${fields.join(', ')})
  VALUES (${prefix}id, ${fields.map(field => folded(source(field, prefix))).join(', ')});`;

export const trackExcludeIndexSchemaSql = `
CREATE TABLE local_track_exclude_fields (
  track_id TEXT NOT NULL PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
  ${fields.map(field => `${field} TEXT`).join(',\n  ')}
) WITHOUT ROWID;
INSERT INTO local_track_exclude_fields(track_id, ${fields.join(', ')})
  SELECT id, ${fields.map(field => folded(source(field))).join(', ')} FROM tracks;
CREATE TRIGGER local_track_exclude_after_insert AFTER INSERT ON tracks BEGIN
  ${put('NEW.')}
END;
CREATE TRIGGER local_track_exclude_after_update AFTER UPDATE OF ${fields.join(', ')} ON tracks
WHEN ${fields.map(field => `OLD.${field} IS NOT NEW.${field}`).join(' OR ')} BEGIN
  ${put('NEW.')}
END;
CREATE TRIGGER local_track_exclude_after_delete AFTER DELETE ON tracks BEGIN
  DELETE FROM local_track_exclude_fields WHERE track_id = OLD.id;
END;
INSERT OR REPLACE INTO library_maintenance_flags(key,value,updated_at)
  VALUES('local_track_query.exclude','1',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
`;

// The scanner uses INSERT ... ON CONFLICT(path) DO UPDATE. Its conflict policy
// overrides a trigger's INSERT OR REPLACE; the derived row already exists on
// this UPDATE path, so update it directly rather than inserting it again.
export const trackExcludeIndexUpdateRepairSql = `
DROP TRIGGER local_track_exclude_after_update;
CREATE TRIGGER local_track_exclude_after_update AFTER UPDATE OF ${fields.join(', ')} ON tracks
WHEN ${fields.map(field => `OLD.${field} IS NOT NEW.${field}`).join(' OR ')} BEGIN
  UPDATE local_track_exclude_fields SET
    ${fields.map(field => `${field} = ${folded(source(field, 'NEW.'))}`).join(',\n    ')}
  WHERE track_id = NEW.id;
END;
`;
