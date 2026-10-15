/**
 * Prefix lengths are bytes. 2–4 cover Latin typeahead; 3, 6 and 9 cover one to
 * three Han characters in UTF-8. Search terms are stored as prefix queries
 * (`term*`). Without these indexes FTS5 walks the whole term list.
 */
export const trackSearchFtsPrefix = '2 3 4 6 9';

const ftsOptions = `tokenize = 'unicode61',
  prefix = '${trackSearchFtsPrefix}'`;

export const tracksFtsSchemaSql = `
CREATE VIRTUAL TABLE IF NOT EXISTS tracks_fts USING fts5(
  title,
  artist,
  album,
  album_artist,
  genre,
  path,
  search_terms,
  ${ftsOptions}
);

CREATE TRIGGER IF NOT EXISTS tracks_fts_after_insert
AFTER INSERT ON tracks
BEGIN
  INSERT INTO tracks_fts(rowid, title, artist, album, album_artist, genre, path, search_terms)
  VALUES (new.rowid, new.title, new.artist, new.album, new.album_artist, COALESCE(new.genre, ''), new.path, new.search_terms);
END;

CREATE TRIGGER IF NOT EXISTS tracks_fts_after_delete
AFTER DELETE ON tracks
BEGIN
  DELETE FROM tracks_fts WHERE rowid = old.rowid;
END;

CREATE TRIGGER IF NOT EXISTS tracks_fts_after_update
AFTER UPDATE OF title, artist, album, album_artist, genre, path, search_terms ON tracks
BEGIN
  DELETE FROM tracks_fts WHERE rowid = old.rowid;
  INSERT INTO tracks_fts(rowid, title, artist, album, album_artist, genre, path, search_terms)
  VALUES (new.rowid, new.title, new.artist, new.album, new.album_artist, COALESCE(new.genre, ''), new.path, new.search_terms);
END;
`;

export const remoteTracksFtsSchemaSql = `
CREATE VIRTUAL TABLE IF NOT EXISTS remote_tracks_fts USING fts5(
  title,
  artist,
  album,
  album_artist,
  genre,
  remote_path,
  search_terms,
  ${ftsOptions}
);

CREATE TRIGGER IF NOT EXISTS remote_tracks_fts_after_insert
AFTER INSERT ON remote_tracks
BEGIN
  INSERT INTO remote_tracks_fts(rowid, title, artist, album, album_artist, genre, remote_path, search_terms)
  VALUES (new.rowid, new.title, new.artist, new.album, new.album_artist, COALESCE(new.genre, ''), new.remote_path, new.search_terms);
END;

CREATE TRIGGER IF NOT EXISTS remote_tracks_fts_after_delete
AFTER DELETE ON remote_tracks
BEGIN
  DELETE FROM remote_tracks_fts WHERE rowid = old.rowid;
END;

CREATE TRIGGER IF NOT EXISTS remote_tracks_fts_after_update
AFTER UPDATE OF title, artist, album, album_artist, genre, remote_path, search_terms ON remote_tracks
BEGIN
  DELETE FROM remote_tracks_fts WHERE rowid = old.rowid;
  INSERT INTO remote_tracks_fts(rowid, title, artist, album, album_artist, genre, remote_path, search_terms)
  VALUES (new.rowid, new.title, new.artist, new.album, new.album_artist, COALESCE(new.genre, ''), new.remote_path, new.search_terms);
END;
`;

export const trackSearchFtsRebuildSql = `
DROP TRIGGER IF EXISTS tracks_fts_after_insert;
DROP TRIGGER IF EXISTS tracks_fts_after_delete;
DROP TRIGGER IF EXISTS tracks_fts_after_update;
DROP TABLE IF EXISTS tracks_fts;
${tracksFtsSchemaSql}
INSERT INTO tracks_fts(rowid, title, artist, album, album_artist, genre, path, search_terms)
SELECT rowid, title, artist, album, album_artist, COALESCE(genre, ''), path, search_terms
FROM tracks;

DROP TRIGGER IF EXISTS remote_tracks_fts_after_insert;
DROP TRIGGER IF EXISTS remote_tracks_fts_after_delete;
DROP TRIGGER IF EXISTS remote_tracks_fts_after_update;
DROP TABLE IF EXISTS remote_tracks_fts;
${remoteTracksFtsSchemaSql}
INSERT INTO remote_tracks_fts(rowid, title, artist, album, album_artist, genre, remote_path, search_terms)
SELECT rowid, title, artist, album, album_artist, COALESCE(genre, ''), remote_path, search_terms
FROM remote_tracks;
`;
