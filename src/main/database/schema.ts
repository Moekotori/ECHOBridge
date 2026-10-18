import { albumCandidateIndexSql } from './albumCandidateIndexSchema';
import { lyricsFilterIndexSchemaSql } from './lyricsFilterIndexSchema';
import { remoteTracksFtsSchemaSql, tracksFtsSchemaSql } from './trackSearchFtsSchema';

export const schemaMigrationTableSql = `
CREATE TABLE IF NOT EXISTS schema_migrations (
  id INTEGER PRIMARY KEY,
  applied_at TEXT NOT NULL
);
`;

// A single-row counter bumped by pure-SQL triggers whenever a local track is
// inserted, deleted, or has its title/artist/missing flag changed. Reading one
// row is how the title-sort read model decides whether its sort keys are stale,
// instead of scanning the whole tracks table; analysis writes that only touch
// updated_at do not move it.
export const localTrackSortKeyRevisionSql = `
CREATE TABLE IF NOT EXISTS local_track_sort_key_revision (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  revision INTEGER NOT NULL DEFAULT 0
);

INSERT OR IGNORE INTO local_track_sort_key_revision (id, revision) VALUES (1, 1);

CREATE TRIGGER IF NOT EXISTS local_track_sort_key_revision_after_insert
AFTER INSERT ON tracks
BEGIN
  UPDATE local_track_sort_key_revision SET revision = revision + 1 WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS local_track_sort_key_revision_after_delete
AFTER DELETE ON tracks
BEGIN
  UPDATE local_track_sort_key_revision SET revision = revision + 1 WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS local_track_sort_key_revision_after_update
AFTER UPDATE OF title, artist, album, missing ON tracks
WHEN old.title IS NOT new.title OR old.artist IS NOT new.artist OR old.album IS NOT new.album OR old.missing IS NOT new.missing
BEGIN
  UPDATE local_track_sort_key_revision SET revision = revision + 1 WHERE id = 1;
END;
`;

export const localAlbumSortKeyRevisionSql = `
CREATE TABLE IF NOT EXISTS local_album_sort_key_revision (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  revision INTEGER NOT NULL DEFAULT 0
);

INSERT OR IGNORE INTO local_album_sort_key_revision (id, revision) VALUES (1, 1);

CREATE TRIGGER IF NOT EXISTS local_album_sort_key_revision_after_insert
AFTER INSERT ON albums
BEGIN
  UPDATE local_album_sort_key_revision SET revision = revision + 1 WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS local_album_sort_key_revision_after_delete
AFTER DELETE ON albums
BEGIN
  UPDATE local_album_sort_key_revision SET revision = revision + 1 WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS local_album_sort_key_revision_after_update
AFTER UPDATE OF title, album_artist ON albums
WHEN old.title IS NOT new.title OR old.album_artist IS NOT new.album_artist
BEGIN
  UPDATE local_album_sort_key_revision SET revision = revision + 1 WHERE id = 1;
END;
`;

export const localArtistSortKeyRevisionSql = `
CREATE TABLE IF NOT EXISTS local_artist_sort_key_revision (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  revision INTEGER NOT NULL DEFAULT 0
);

INSERT OR IGNORE INTO local_artist_sort_key_revision (id, revision) VALUES (1, 1);

CREATE TRIGGER IF NOT EXISTS local_artist_sort_key_revision_after_insert
AFTER INSERT ON artists
BEGIN
  UPDATE local_artist_sort_key_revision SET revision = revision + 1 WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS local_artist_sort_key_revision_after_delete
AFTER DELETE ON artists
BEGIN
  UPDATE local_artist_sort_key_revision SET revision = revision + 1 WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS local_artist_sort_key_revision_after_update
AFTER UPDATE OF name, sort_name ON artists
WHEN old.name IS NOT new.name OR old.sort_name IS NOT new.sort_name
BEGIN
  UPDATE local_artist_sort_key_revision SET revision = revision + 1 WHERE id = 1;
END;
`;

export const localGenrePageRevisionSql = `
CREATE TABLE IF NOT EXISTS local_genre_page_revision (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  revision INTEGER NOT NULL DEFAULT 0
);

INSERT OR IGNORE INTO local_genre_page_revision (id, revision) VALUES (1, 1);

CREATE TRIGGER IF NOT EXISTS local_genre_page_revision_after_insert
AFTER INSERT ON tracks
BEGIN
  UPDATE local_genre_page_revision SET revision = revision + 1 WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS local_genre_page_revision_after_delete
AFTER DELETE ON tracks
BEGIN
  UPDATE local_genre_page_revision SET revision = revision + 1 WHERE id = 1;
END;

DROP TRIGGER IF EXISTS local_genre_page_revision_after_update;
CREATE TRIGGER IF NOT EXISTS local_genre_page_revision_after_update
AFTER UPDATE OF genre, missing, cover_id, album, album_artist ON tracks
WHEN old.genre IS NOT new.genre
  OR old.missing IS NOT new.missing
  OR old.cover_id IS NOT new.cover_id
  OR old.album IS NOT new.album
  OR old.album_artist IS NOT new.album_artist
BEGIN
  UPDATE local_genre_page_revision SET revision = revision + 1 WHERE id = 1;
END;
`;

export const localGenrePagesSql = `
CREATE TABLE IF NOT EXISTS local_genre_pages (
  genre_key TEXT PRIMARY KEY,
  display_name TEXT NOT NULL,
  name_key TEXT NOT NULL,
  name_text TEXT NOT NULL,
  track_count INTEGER NOT NULL,
  album_count INTEGER NOT NULL,
  cover_id TEXT,
  unclassified INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_local_genre_pages_name ON local_genre_pages(
  name_key COLLATE NOCASE,
  name_text COLLATE NOCASE,
  genre_key
);

CREATE INDEX IF NOT EXISTS idx_local_genre_pages_track_count ON local_genre_pages(
  track_count,
  name_key COLLATE NOCASE,
  genre_key
);
`;

export const librarySchemaSql = `
CREATE TABLE IF NOT EXISTS library_maintenance_flags (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS folders (
  id TEXT PRIMARY KEY,
  path TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'active',
  enabled INTEGER NOT NULL DEFAULT 1,
  import_profile TEXT NOT NULL DEFAULT 'normal',
  last_scan_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS covers (
  id TEXT PRIMARY KEY,
  source_type TEXT NOT NULL,
  source_hash TEXT,
  mime_type TEXT,
  thumb_path TEXT,
  album_path TEXT,
  large_path TEXT,
  original_ref TEXT,
  cache_version INTEGER,
  warnings_json TEXT NOT NULL DEFAULT '[]',
  errors_json TEXT NOT NULL DEFAULT '[]',
  cover_thumb TEXT,
  cover_large TEXT,
  cover_original TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS tracks (
  id TEXT PRIMARY KEY,
  path TEXT NOT NULL UNIQUE,
  folder_id TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  mtime_ms INTEGER NOT NULL,
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  album TEXT NOT NULL,
  album_artist TEXT NOT NULL,
  track_no INTEGER,
  disc_no INTEGER,
  year INTEGER,
  genre TEXT,
  duration REAL NOT NULL DEFAULT 0,
  codec TEXT,
  sample_rate INTEGER,
  bit_depth INTEGER,
  bitrate INTEGER,
  bpm REAL,
  bpm_confidence REAL,
  beat_offset_ms REAL,
  analysis_status TEXT NOT NULL DEFAULT 'none',
  analysis_version INTEGER NOT NULL DEFAULT 0,
  analysis_error TEXT,
  analysis_updated_at TEXT,
  replay_gain_track_gain_db REAL,
  replay_gain_album_gain_db REAL,
  replay_gain_track_peak REAL,
  replay_gain_album_peak REAL,
  replay_gain_integrated_lufs REAL,
  replay_gain_source TEXT NOT NULL DEFAULT 'none',
  replay_gain_status TEXT NOT NULL DEFAULT 'none',
  replay_gain_version INTEGER NOT NULL DEFAULT 0,
  replay_gain_error TEXT,
  replay_gain_updated_at TEXT,
  file_identity TEXT,
  file_identity_source TEXT,
  quick_hash TEXT,
  quick_hash_version INTEGER,
  identity_status TEXT,
  identity_updated_at TEXT,
  identity_error TEXT,
  search_terms TEXT NOT NULL DEFAULT '',
  cover_id TEXT,
  metadata_status TEXT NOT NULL DEFAULT 'ok',
  embedded_metadata_status TEXT NOT NULL DEFAULT 'pending',
  embedded_cover_status TEXT NOT NULL DEFAULT 'pending',
  network_metadata_status TEXT NOT NULL DEFAULT 'none',
  field_sources_json TEXT NOT NULL,
  play_count INTEGER NOT NULL DEFAULT 0,
  last_played_at TEXT,
  recommendation_preference TEXT NOT NULL DEFAULT 'neutral',
  skip_count INTEGER NOT NULL DEFAULT 0,
  last_skipped_at TEXT,
  missing INTEGER NOT NULL DEFAULT 0,
  library_hidden INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE CASCADE,
  FOREIGN KEY (cover_id) REFERENCES covers(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS local_track_sort_keys (
  track_id TEXT PRIMARY KEY,
  title_key TEXT NOT NULL,
  title_text TEXT NOT NULL,
  artist_key TEXT NOT NULL,
  artist_text TEXT NOT NULL,
  album_key TEXT NOT NULL DEFAULT '',
  album_text TEXT NOT NULL DEFAULT '',
  track_updated_at TEXT NOT NULL,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_local_track_sort_keys_title ON local_track_sort_keys(
  title_key COLLATE NOCASE,
  title_text COLLATE NOCASE,
  artist_key COLLATE NOCASE,
  artist_text COLLATE NOCASE,
  track_id
);
${localTrackSortKeyRevisionSql}

CREATE TABLE IF NOT EXISTS embedded_tag_write_covers (
  content_hash TEXT PRIMARY KEY,
  mime_type TEXT NOT NULL,
  data BLOB NOT NULL,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS embedded_tag_write_jobs (
  track_id TEXT PRIMARY KEY,
  version INTEGER NOT NULL,
  file_path TEXT NOT NULL,
  tags_json TEXT NOT NULL,
  cover_hash TEXT,
  error_prefix TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending',
  attempt INTEGER NOT NULL DEFAULT 0,
  last_error TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE,
  FOREIGN KEY (cover_hash) REFERENCES embedded_tag_write_covers(content_hash) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_embedded_tag_write_jobs_status_updated
  ON embedded_tag_write_jobs(status, updated_at);

${tracksFtsSchemaSql}

CREATE TABLE IF NOT EXISTS albums (
  id TEXT PRIMARY KEY,
  album_key TEXT NOT NULL UNIQUE,
  title TEXT NOT NULL,
  album_artist TEXT NOT NULL,
  year INTEGER,
  cover_id TEXT,
  user_rating INTEGER,
  track_count INTEGER NOT NULL DEFAULT 0,
  duration REAL NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  CHECK (user_rating IS NULL OR user_rating BETWEEN 1 AND 5),
  FOREIGN KEY (cover_id) REFERENCES covers(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS album_tracks (
  album_id TEXT NOT NULL,
  track_id TEXT NOT NULL,
  disc_no INTEGER,
  track_no INTEGER,
  position INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (album_id, track_id),
  FOREIGN KEY (album_id) REFERENCES albums(id) ON DELETE CASCADE,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS artists (
  id TEXT PRIMARY KEY,
  artist_key TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  sort_name TEXT,
  role TEXT NOT NULL DEFAULT 'track',
  track_count INTEGER NOT NULL DEFAULT 0,
  album_count INTEGER NOT NULL DEFAULT 0,
  cover_id TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (cover_id) REFERENCES covers(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS local_album_sort_keys (
  album_id TEXT PRIMARY KEY,
  title_key TEXT NOT NULL,
  title_text TEXT NOT NULL,
  artist_key TEXT NOT NULL,
  artist_text TEXT NOT NULL,
  album_updated_at TEXT NOT NULL,
  FOREIGN KEY (album_id) REFERENCES albums(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_local_album_sort_keys_title ON local_album_sort_keys(
  title_key COLLATE NOCASE,
  title_text COLLATE NOCASE,
  artist_key COLLATE NOCASE,
  artist_text COLLATE NOCASE,
  album_id
);
${localAlbumSortKeyRevisionSql}

CREATE TABLE IF NOT EXISTS local_artist_sort_keys (
  artist_id TEXT PRIMARY KEY,
  name_key TEXT NOT NULL,
  name_text TEXT NOT NULL,
  artist_updated_at TEXT NOT NULL,
  FOREIGN KEY (artist_id) REFERENCES artists(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_local_artist_sort_keys_name ON local_artist_sort_keys(
  name_key COLLATE NOCASE,
  name_text COLLATE NOCASE,
  artist_id
);
${localArtistSortKeyRevisionSql}
${localGenrePagesSql}
${localGenrePageRevisionSql}

CREATE TABLE IF NOT EXISTS artist_image_cache (
  artist_key TEXT PRIMARY KEY,
  artist_name TEXT NOT NULL,
  provider TEXT NOT NULL,
  provider_artist_id TEXT,
  source_url TEXT,
  source_hash TEXT,
  thumb_path TEXT,
  medium_path TEXT,
  large_path TEXT,
  status TEXT NOT NULL DEFAULT 'pending',
  confidence REAL DEFAULT 0,
  failure_reason TEXT,
  fetched_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS artist_tracks (
  artist_id TEXT NOT NULL,
  track_id TEXT NOT NULL,
  source_name TEXT NOT NULL,
  position INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (artist_id, track_id),
  FOREIGN KEY (artist_id) REFERENCES artists(id) ON DELETE CASCADE,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS artist_albums (
  artist_id TEXT NOT NULL,
  album_id TEXT NOT NULL,
  source_name TEXT NOT NULL,
  PRIMARY KEY (artist_id, album_id),
  FOREIGN KEY (artist_id) REFERENCES artists(id) ON DELETE CASCADE,
  FOREIGN KEY (album_id) REFERENCES albums(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS artist_index_refresh_queue (
  entity_kind TEXT NOT NULL,
  entity_id TEXT NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY (entity_kind, entity_id),
  CHECK (entity_kind IN ('track', 'album'))
);

CREATE INDEX IF NOT EXISTS idx_artist_index_refresh_queue_created
  ON artist_index_refresh_queue(created_at, entity_kind, entity_id);

-- Album identification and display metadata intentionally live outside the
-- derived albums table lifecycle. Album grouping refreshes rebuild albums in a
-- transaction, while pinned identities and user overrides must survive.
CREATE TABLE IF NOT EXISTS album_metadata_profiles (
  album_id TEXT PRIMARY KEY,
  provider TEXT,
  provider_item_id TEXT,
  release_group_id TEXT,
  identity_state TEXT NOT NULL DEFAULT 'unidentified',
  online_values_json TEXT NOT NULL DEFAULT '{}',
  user_values_json TEXT NOT NULL DEFAULT '{}',
  source_snapshot_json TEXT NOT NULL DEFAULT '{}',
  track_mapping_json TEXT NOT NULL DEFAULT '[]',
  identified_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  CHECK (identity_state IN ('unidentified', 'pinned'))
);

CREATE INDEX IF NOT EXISTS idx_album_metadata_profiles_provider_item
  ON album_metadata_profiles(provider, provider_item_id)
  WHERE provider IS NOT NULL AND provider_item_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS music_people (
  id TEXT PRIMARY KEY,
  person_key TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  sort_name TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS music_credit_roles (
  id TEXT PRIMARY KEY,
  role_key TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  category TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS music_works (
  id TEXT PRIMARY KEY,
  work_key TEXT NOT NULL UNIQUE,
  title TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS music_movements (
  id TEXT PRIMARY KEY,
  movement_key TEXT NOT NULL UNIQUE,
  work_id TEXT,
  title TEXT NOT NULL,
  movement_no INTEGER,
  movement_total INTEGER,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (work_id) REFERENCES music_works(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS music_performances (
  id TEXT PRIMARY KEY,
  performance_key TEXT NOT NULL UNIQUE,
  work_id TEXT,
  movement_id TEXT,
  title TEXT NOT NULL,
  performance_date TEXT,
  venue TEXT,
  is_synthetic INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (work_id) REFERENCES music_works(id) ON DELETE SET NULL,
  FOREIGN KEY (movement_id) REFERENCES music_movements(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS music_labels (
  id TEXT PRIMARY KEY,
  label_key TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS track_music_graph (
  track_id TEXT PRIMARY KEY,
  work_id TEXT,
  movement_id TEXT,
  performance_id TEXT,
  source_json TEXT NOT NULL DEFAULT '{}',
  updated_at TEXT NOT NULL,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE,
  FOREIGN KEY (work_id) REFERENCES music_works(id) ON DELETE SET NULL,
  FOREIGN KEY (movement_id) REFERENCES music_movements(id) ON DELETE SET NULL,
  FOREIGN KEY (performance_id) REFERENCES music_performances(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS music_track_labels (
  track_id TEXT NOT NULL,
  label_id TEXT NOT NULL,
  position INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (track_id, label_id),
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE,
  FOREIGN KEY (label_id) REFERENCES music_labels(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS music_credits (
  id TEXT PRIMARY KEY,
  track_id TEXT NOT NULL,
  person_id TEXT NOT NULL,
  role_id TEXT NOT NULL,
  target_type TEXT NOT NULL,
  target_id TEXT NOT NULL,
  credited_as TEXT NOT NULL,
  instrument TEXT,
  position INTEGER NOT NULL DEFAULT 0,
  source TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE,
  FOREIGN KEY (person_id) REFERENCES music_people(id) ON DELETE CASCADE,
  FOREIGN KEY (role_id) REFERENCES music_credit_roles(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_music_movements_work ON music_movements(work_id, movement_no);
CREATE INDEX IF NOT EXISTS idx_music_performances_work ON music_performances(work_id, movement_id);
CREATE INDEX IF NOT EXISTS idx_music_track_labels_label ON music_track_labels(label_id, track_id);
CREATE INDEX IF NOT EXISTS idx_music_credits_person ON music_credits(person_id, role_id);
CREATE INDEX IF NOT EXISTS idx_music_credits_track ON music_credits(track_id, position);
CREATE INDEX IF NOT EXISTS idx_music_credits_role ON music_credits(role_id, target_type, target_id);
CREATE INDEX IF NOT EXISTS idx_music_credits_target ON music_credits(target_type, target_id, position);

CREATE TABLE IF NOT EXISTS scan_jobs (
  id TEXT PRIMARY KEY,
  folder_id TEXT NOT NULL,
  status TEXT NOT NULL,
  phase TEXT NOT NULL DEFAULT 'queued',
  discovered_count INTEGER NOT NULL DEFAULT 0,
  parsed_count INTEGER NOT NULL DEFAULT 0,
  skipped_count INTEGER NOT NULL DEFAULT 0,
  cover_count INTEGER NOT NULL DEFAULT 0,
  total_files INTEGER NOT NULL DEFAULT 0,
  processed_files INTEGER NOT NULL DEFAULT 0,
  skipped_files INTEGER NOT NULL DEFAULT 0,
  added_tracks INTEGER NOT NULL DEFAULT 0,
  updated_tracks INTEGER NOT NULL DEFAULT 0,
  removed_tracks INTEGER NOT NULL DEFAULT 0,
  error_count INTEGER NOT NULL DEFAULT 0,
  errors_json TEXT NOT NULL DEFAULT '[]',
  cancel_requested INTEGER NOT NULL DEFAULT 0,
  started_at TEXT,
  finished_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS scan_directory_snapshots (
  folder_id TEXT NOT NULL,
  path TEXT NOT NULL,
  mtime_ms INTEGER NOT NULL,
  entries_json TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  PRIMARY KEY (folder_id, path),
  FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_scan_directory_snapshots_folder
  ON scan_directory_snapshots(folder_id);

CREATE TABLE IF NOT EXISTS library_inbox_batches (
  id TEXT PRIMARY KEY,
  scan_job_id TEXT NOT NULL UNIQUE,
  folder_id TEXT NOT NULL,
  folder_name TEXT NOT NULL,
  folder_path TEXT NOT NULL,
  added_count INTEGER NOT NULL DEFAULT 0,
  missing_cover_count INTEGER NOT NULL DEFAULT 0,
  metadata_issue_count INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  finished_at TEXT NOT NULL,
  FOREIGN KEY (scan_job_id) REFERENCES scan_jobs(id) ON DELETE CASCADE,
  FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS library_inbox_items (
  batch_id TEXT NOT NULL,
  track_id TEXT NOT NULL,
  position INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY (batch_id, track_id),
  FOREIGN KEY (batch_id) REFERENCES library_inbox_batches(id) ON DELETE CASCADE,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS library_inbox_item_states (
  batch_id TEXT NOT NULL,
  track_id TEXT NOT NULL,
  status TEXT NOT NULL CHECK (status IN ('pending', 'processed', 'ignored')),
  updated_at TEXT NOT NULL,
  PRIMARY KEY (batch_id, track_id),
  FOREIGN KEY (batch_id) REFERENCES library_inbox_batches(id) ON DELETE CASCADE,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_library_inbox_batches_finished_at
  ON library_inbox_batches(finished_at DESC);
CREATE INDEX IF NOT EXISTS idx_library_inbox_items_batch_position
  ON library_inbox_items(batch_id, position);
CREATE INDEX IF NOT EXISTS idx_library_inbox_items_track_id
  ON library_inbox_items(track_id);
CREATE INDEX IF NOT EXISTS idx_library_inbox_item_states_status
  ON library_inbox_item_states(status);

CREATE TABLE IF NOT EXISTS playback_history (
  id TEXT PRIMARY KEY,
  track_id TEXT,
  track_path TEXT NOT NULL,
  media_type TEXT NOT NULL DEFAULT 'local',
  provider TEXT,
  provider_track_id TEXT,
  stable_key TEXT,
  title_snapshot TEXT,
  artist_snapshot TEXT,
  album_snapshot TEXT,
  duration_snapshot REAL,
  cover_snapshot TEXT,
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  album TEXT,
  album_artist TEXT,
  cover_id TEXT,
  started_at TEXT NOT NULL,
  ended_at TEXT,
  played_seconds REAL NOT NULL DEFAULT 0,
  duration_seconds REAL NOT NULL DEFAULT 0,
  completed INTEGER NOT NULL DEFAULT 0,
  source_type TEXT,
  source_label TEXT,
  queue_id TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS playback_history_stats (
  history_key TEXT PRIMARY KEY,
  track_id TEXT,
  track_path TEXT NOT NULL,
  media_type TEXT NOT NULL DEFAULT 'local',
  provider TEXT,
  provider_track_id TEXT,
  stable_key TEXT,
  title_snapshot TEXT,
  artist_snapshot TEXT,
  album_snapshot TEXT,
  duration_snapshot REAL,
  cover_snapshot TEXT,
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  album TEXT,
  album_artist TEXT,
  cover_id TEXT,
  play_count INTEGER NOT NULL DEFAULT 0,
  completed_count INTEGER NOT NULL DEFAULT 0,
  total_played_seconds REAL NOT NULL DEFAULT 0,
  duration_seconds REAL NOT NULL DEFAULT 0,
  last_started_at TEXT NOT NULL,
  last_ended_at TEXT,
  source_type TEXT,
  source_label TEXT,
  queue_id TEXT,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS steam_achievement_playback_facts (
  id TEXT PRIMARY KEY,
  track_id TEXT NOT NULL,
  artist TEXT NOT NULL DEFAULT '',
  started_at TEXT NOT NULL,
  ended_at TEXT NOT NULL,
  played_seconds REAL NOT NULL DEFAULT 0,
  duration_seconds REAL NOT NULL DEFAULT 0,
  qualified_completion INTEGER NOT NULL DEFAULT 0,
  output_mode TEXT,
  source TEXT NOT NULL DEFAULT 'audio-core'
);

CREATE INDEX IF NOT EXISTS idx_steam_achievement_facts_track
  ON steam_achievement_playback_facts(track_id, qualified_completion);
CREATE INDEX IF NOT EXISTS idx_steam_achievement_facts_ended
  ON steam_achievement_playback_facts(ended_at);

CREATE TABLE IF NOT EXISTS playlists (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  description TEXT,
  kind TEXT NOT NULL DEFAULT 'manual',
  source_provider TEXT NOT NULL DEFAULT 'local',
  source_playlist_id TEXT,
  cover_id TEXT,
  cover_url TEXT,
  sort_mode TEXT NOT NULL DEFAULT 'manual',
  item_count INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS playlist_items (
  id TEXT PRIMARY KEY,
  playlist_id TEXT NOT NULL,
  media_type TEXT NOT NULL,
  media_id TEXT,
  source_provider TEXT NOT NULL DEFAULT 'local',
  source_item_id TEXT,
  title_snapshot TEXT,
  artist_snapshot TEXT,
  album_snapshot TEXT,
  duration_snapshot REAL,
  cover_id TEXT,
  position INTEGER NOT NULL,
  added_at TEXT NOT NULL,
  added_from TEXT,
  unavailable INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS smart_playlist_rules (
  playlist_id TEXT PRIMARY KEY,
  version INTEGER NOT NULL DEFAULT 1,
  rules_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS track_integrity_checks (
  track_id TEXT PRIMARY KEY,
  status TEXT NOT NULL,
  checked_at TEXT NOT NULL,
  error TEXT,
  size_bytes INTEGER,
  mtime_ms INTEGER,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS network_metadata_candidates (
  id TEXT PRIMARY KEY,
  track_id TEXT NOT NULL,
  album_id TEXT,
  provider TEXT NOT NULL,
  provider_item_id TEXT NOT NULL,
  title TEXT,
  artist TEXT,
  album TEXT,
  album_artist TEXT,
  year INTEGER,
  genre TEXT,
  duration REAL,
  track_no INTEGER,
  disc_no INTEGER,
  cover_url TEXT,
  score REAL NOT NULL,
  raw_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE,
  FOREIGN KEY (album_id) REFERENCES albums(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS network_metadata_decisions (
  id TEXT PRIMARY KEY,
  track_id TEXT NOT NULL,
  candidate_id TEXT NOT NULL,
  decision TEXT NOT NULL,
  applied_fields_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE,
  FOREIGN KEY (candidate_id) REFERENCES network_metadata_candidates(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS network_cover_candidates (
  id TEXT PRIMARY KEY,
  track_id TEXT,
  album_id TEXT,
  provider TEXT NOT NULL,
  cover_url TEXT NOT NULL,
  width INTEGER,
  height INTEGER,
  mime_type TEXT,
  score REAL NOT NULL,
  cached_thumb_path TEXT,
  cached_large_path TEXT,
  raw_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE,
  FOREIGN KEY (album_id) REFERENCES albums(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS album_online_info_cache (
  cache_key TEXT PRIMARY KEY,
  album_id TEXT NOT NULL,
  normalized_title TEXT NOT NULL,
  normalized_artist TEXT NOT NULL,
  credits_json TEXT NOT NULL,
  information_json TEXT,
  match_json TEXT,
  sources_json TEXT NOT NULL DEFAULT '[]',
  provider_errors_json TEXT NOT NULL DEFAULT '[]',
  status TEXT NOT NULL,
  fetched_at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS artist_online_info_cache (
  cache_key TEXT PRIMARY KEY,
  artist_id TEXT NOT NULL,
  normalized_name TEXT NOT NULL,
  locale TEXT NOT NULL,
  region TEXT,
  bio_json TEXT,
  identity_json TEXT,
  image_credits_json TEXT NOT NULL DEFAULT '[]',
  external_links_json TEXT NOT NULL DEFAULT '[]',
  related_artists_json TEXT NOT NULL DEFAULT '[]',
  source_labels_json TEXT NOT NULL DEFAULT '[]',
  provider_errors_json TEXT NOT NULL DEFAULT '[]',
  status TEXT NOT NULL,
  fetched_at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS artist_event_cache (
  cache_key TEXT PRIMARY KEY,
  artist_id TEXT,
  normalized_name TEXT NOT NULL,
  region TEXT,
  source TEXT NOT NULL,
  events_json TEXT NOT NULL DEFAULT '[]',
  sources_json TEXT NOT NULL DEFAULT '[]',
  status TEXT NOT NULL,
  message TEXT,
  fetched_at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS lyrics_cache (
  id TEXT PRIMARY KEY,
  cache_key TEXT NOT NULL UNIQUE,
  track_id TEXT,
  provider TEXT NOT NULL,
  provider_lyrics_id TEXT,
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  album TEXT,
  duration_seconds REAL,
  kind TEXT NOT NULL,
  plain_lyrics TEXT,
  synced_lyrics TEXT,
  lines_json TEXT NOT NULL,
  offset_ms INTEGER NOT NULL DEFAULT 0,
  score REAL,
  acceptance_origin TEXT NOT NULL DEFAULT 'legacy',
  match_policy_version INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

${lyricsFilterIndexSchemaSql}

CREATE TABLE IF NOT EXISTS lyrics_candidates (
  id TEXT PRIMARY KEY,
  track_id TEXT,
  provider TEXT NOT NULL,
  provider_lyrics_id TEXT,
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  album TEXT,
  duration_seconds REAL,
  instrumental INTEGER NOT NULL DEFAULT 0,
  has_synced INTEGER NOT NULL DEFAULT 0,
  has_plain INTEGER NOT NULL DEFAULT 0,
  score REAL NOT NULL,
  risk TEXT,
  reasons_json TEXT,
  title_score REAL,
  artist_score REAL,
  album_score REAL,
  duration_score REAL,
  version_score REAL,
  source_label TEXT NOT NULL,
  raw_json TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS lyrics_backfill_jobs (
  id TEXT PRIMARY KEY,
  mode TEXT NOT NULL,
  status TEXT NOT NULL,
  phase TEXT NOT NULL,
  status_json TEXT NOT NULL,
  options_json TEXT NOT NULL,
  target_ids_json TEXT NOT NULL DEFAULT '[]',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  finished_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_lyrics_backfill_jobs_status_updated
  ON lyrics_backfill_jobs(status, updated_at);

CREATE TABLE IF NOT EXISTS lyrics_backfill_job_attempts (
  job_id TEXT NOT NULL,
  track_id TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'processed',
  updated_at TEXT NOT NULL,
  PRIMARY KEY (job_id, track_id),
  FOREIGN KEY (job_id) REFERENCES lyrics_backfill_jobs(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS duplicate_track_groups (
  id TEXT PRIMARY KEY,
  mode TEXT NOT NULL,
  duplicate_key TEXT NOT NULL,
  representative_track_id TEXT NOT NULL,
  track_count INTEGER NOT NULL,
  hidden_count INTEGER NOT NULL DEFAULT 0,
  confidence REAL NOT NULL DEFAULT 1.0,
  reasons_json TEXT NOT NULL DEFAULT '[]',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(mode, duplicate_key)
);

CREATE TABLE IF NOT EXISTS duplicate_track_members (
  group_id TEXT NOT NULL,
  track_id TEXT NOT NULL,
  quality_score REAL NOT NULL,
  rank INTEGER NOT NULL,
  hidden INTEGER NOT NULL DEFAULT 0,
  reasons_json TEXT NOT NULL DEFAULT '[]',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  PRIMARY KEY(group_id, track_id)
);

CREATE TABLE IF NOT EXISTS track_videos (
  id TEXT PRIMARY KEY,
  track_id TEXT NOT NULL,
  provider TEXT NOT NULL,
  source_type TEXT NOT NULL,
  source_id TEXT,
  title TEXT,
  artist TEXT,
  url TEXT,
  provider_url TEXT,
  thumbnail_url TEXT,
  file_path TEXT,
  mime_type TEXT,
  duration_seconds REAL,
  width INTEGER,
  height INTEGER,
  selected_quality_id TEXT,
  quality_label TEXT,
  fps REAL,
  offset_ms INTEGER NOT NULL DEFAULT 0,
  raw_provider_json TEXT,
  score REAL NOT NULL DEFAULT 0,
  selected INTEGER NOT NULL DEFAULT 0,
  selection_origin TEXT NOT NULL DEFAULT 'unknown',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS track_video_streams (
  id TEXT PRIMARY KEY,
  video_id TEXT NOT NULL,
  provider TEXT NOT NULL,
  variant_id TEXT NOT NULL,
  label TEXT NOT NULL,
  quality_tier TEXT NOT NULL,
  width INTEGER,
  height INTEGER,
  fps REAL,
  codec TEXT,
  container TEXT,
  mime_type TEXT,
  protocol TEXT NOT NULL,
  url TEXT,
  headers_json TEXT NOT NULL DEFAULT '{}',
  playable_in_app INTEGER NOT NULL DEFAULT 0,
  requires_account INTEGER NOT NULL DEFAULT 0,
  expires_at TEXT,
  raw_json TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (video_id) REFERENCES track_videos(id) ON DELETE CASCADE,
  UNIQUE(video_id, variant_id)
);

CREATE TABLE IF NOT EXISTS remote_sources (
  id TEXT PRIMARY KEY,
  provider TEXT NOT NULL,
  display_name TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'enabled',
  base_url TEXT,
  username TEXT,
  auth_type TEXT NOT NULL DEFAULT 'basic',
  encrypted_secret TEXT,
  config_json TEXT NOT NULL DEFAULT '{}',
  sync_mode TEXT NOT NULL DEFAULT 'index',
  last_test_at TEXT,
  last_sync_at TEXT,
  last_error TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS remote_tracks (
  id TEXT PRIMARY KEY,
  source_id TEXT NOT NULL,
  provider TEXT NOT NULL,
  remote_path TEXT NOT NULL,
  remote_url_hash TEXT NOT NULL,
  stable_key TEXT NOT NULL,
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  album TEXT NOT NULL,
  album_artist TEXT NOT NULL,
  track_no INTEGER,
  disc_no INTEGER,
  year INTEGER,
  genre TEXT,
  duration REAL,
  codec TEXT,
  sample_rate INTEGER,
  bit_depth INTEGER,
  bitrate INTEGER,
  size_bytes INTEGER,
  modified_at TEXT,
  etag TEXT,
  cover_id TEXT,
  cover_status TEXT NOT NULL DEFAULT 'pending',
  metadata_status TEXT NOT NULL DEFAULT 'pending',
  lyrics_status TEXT NOT NULL DEFAULT 'pending',
  mv_status TEXT NOT NULL DEFAULT 'pending',
  availability TEXT NOT NULL DEFAULT 'unknown',
  field_sources_json TEXT NOT NULL DEFAULT '{}',
  search_terms TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (source_id) REFERENCES remote_sources(id) ON DELETE CASCADE,
  FOREIGN KEY (cover_id) REFERENCES covers(id) ON DELETE SET NULL,
  UNIQUE(source_id, remote_path),
  UNIQUE(source_id, stable_key)
);

CREATE TABLE IF NOT EXISTS remote_cover_cache (
  cache_key TEXT PRIMARY KEY,
  provider TEXT NOT NULL,
  cover_id TEXT NOT NULL,
  source_id TEXT,
  cover_art TEXT,
  remote_path TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (cover_id) REFERENCES covers(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS remote_provider_scan_cache (
  source_id TEXT NOT NULL,
  namespace TEXT NOT NULL,
  cache_key TEXT NOT NULL,
  fingerprint TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  verified_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  PRIMARY KEY (source_id, namespace, cache_key),
  FOREIGN KEY (source_id) REFERENCES remote_sources(id) ON DELETE CASCADE
);

${remoteTracksFtsSchemaSql}

CREATE TABLE IF NOT EXISTS streaming_tracks (
  id TEXT PRIMARY KEY,
  provider TEXT NOT NULL,
  provider_track_id TEXT NOT NULL,
  stable_key TEXT NOT NULL,
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  album TEXT NOT NULL,
  album_id TEXT,
  album_artist TEXT,
  duration REAL,
  cover_url TEXT,
  cover_id TEXT,
  qualities_json TEXT NOT NULL DEFAULT '[]',
  playable INTEGER NOT NULL DEFAULT 1,
  unavailable_reason TEXT,
  lyrics_status TEXT NOT NULL DEFAULT 'unknown',
  mv_status TEXT NOT NULL DEFAULT 'unknown',
  raw_json TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(provider, provider_track_id),
  UNIQUE(stable_key)
);

CREATE TABLE IF NOT EXISTS streaming_api_cache (
  cache_key TEXT PRIMARY KEY,
  provider TEXT NOT NULL,
  kind TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_tracks_path ON tracks(path);
CREATE INDEX IF NOT EXISTS idx_tracks_folder_id ON tracks(folder_id);
CREATE INDEX IF NOT EXISTS idx_tracks_title ON tracks(title);
CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist);
CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks(album);
CREATE INDEX IF NOT EXISTS idx_tracks_genre ON tracks(genre);
CREATE INDEX IF NOT EXISTS idx_tracks_sample_rate ON tracks(sample_rate) WHERE missing = 0;
CREATE INDEX IF NOT EXISTS idx_tracks_library_hidden ON tracks(updated_at, id) WHERE library_hidden = 1;
CREATE INDEX IF NOT EXISTS idx_tracks_analysis_status ON tracks(analysis_status);

CREATE TABLE IF NOT EXISTS audio_transition_analysis (
  track_id TEXT NOT NULL,
  fingerprint TEXT NOT NULL,
  analyzer_version INTEGER NOT NULL,
  status TEXT NOT NULL,
  analysis_json TEXT NOT NULL,
  error TEXT,
  analyzed_at TEXT,
  updated_at TEXT NOT NULL,
  PRIMARY KEY (track_id, fingerprint, analyzer_version),
  FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_audio_transition_analysis_status
  ON audio_transition_analysis(status, updated_at);
CREATE INDEX IF NOT EXISTS idx_tracks_file_identity ON tracks(file_identity) WHERE file_identity IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_tracks_quick_hash ON tracks(quick_hash) WHERE quick_hash IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_albums_album_key ON albums(album_key);
CREATE INDEX IF NOT EXISTS idx_album_tracks_album_id ON album_tracks(album_id);
CREATE INDEX IF NOT EXISTS idx_album_tracks_track_id ON album_tracks(track_id);
CREATE INDEX IF NOT EXISTS idx_artist_tracks_artist_id ON artist_tracks(artist_id);
CREATE INDEX IF NOT EXISTS idx_artist_tracks_track_id ON artist_tracks(track_id);
CREATE INDEX IF NOT EXISTS idx_artist_albums_artist_id ON artist_albums(artist_id);
CREATE INDEX IF NOT EXISTS idx_artist_albums_album_id ON artist_albums(album_id);
CREATE INDEX IF NOT EXISTS idx_artist_image_cache_status ON artist_image_cache(status);
CREATE INDEX IF NOT EXISTS idx_artist_image_cache_provider_id ON artist_image_cache(provider, provider_artist_id);
CREATE INDEX IF NOT EXISTS idx_folders_path ON folders(path);
CREATE INDEX IF NOT EXISTS idx_covers_id ON covers(id);
CREATE INDEX IF NOT EXISTS idx_covers_source_hash ON covers(source_hash);
CREATE INDEX IF NOT EXISTS idx_network_metadata_candidates_track_id ON network_metadata_candidates(track_id);
CREATE INDEX IF NOT EXISTS idx_network_metadata_decisions_track_id ON network_metadata_decisions(track_id);
CREATE INDEX IF NOT EXISTS idx_network_cover_candidates_track_id ON network_cover_candidates(track_id);
CREATE INDEX IF NOT EXISTS idx_album_online_info_cache_album_id ON album_online_info_cache(album_id);
CREATE INDEX IF NOT EXISTS idx_artist_online_info_cache_artist_id ON artist_online_info_cache(artist_id);
CREATE INDEX IF NOT EXISTS idx_artist_event_cache_artist_id ON artist_event_cache(artist_id);
CREATE INDEX IF NOT EXISTS idx_artist_event_cache_source ON artist_event_cache(source);
CREATE INDEX IF NOT EXISTS idx_lyrics_cache_track_provider ON lyrics_cache(track_id, provider);
CREATE INDEX IF NOT EXISTS idx_lyrics_cache_cache_key ON lyrics_cache(cache_key);
CREATE INDEX IF NOT EXISTS idx_lyrics_candidates_track_provider_status ON lyrics_candidates(track_id, provider, status);
CREATE INDEX IF NOT EXISTS idx_duplicate_members_track_id ON duplicate_track_members(track_id);
CREATE INDEX IF NOT EXISTS idx_duplicate_members_group_rank ON duplicate_track_members(group_id, rank);
CREATE INDEX IF NOT EXISTS idx_duplicate_groups_representative ON duplicate_track_groups(representative_track_id);
CREATE INDEX IF NOT EXISTS idx_duplicate_members_hidden ON duplicate_track_members(hidden);
CREATE INDEX IF NOT EXISTS idx_track_videos_track_id ON track_videos(track_id);
CREATE INDEX IF NOT EXISTS idx_track_videos_track_selected ON track_videos(track_id, selected);
CREATE INDEX IF NOT EXISTS idx_track_videos_provider_source ON track_videos(provider, source_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_track_videos_one_selected ON track_videos(track_id) WHERE selected = 1;
CREATE INDEX IF NOT EXISTS idx_track_video_streams_video_id ON track_video_streams(video_id);
CREATE INDEX IF NOT EXISTS idx_track_video_streams_provider ON track_video_streams(provider, variant_id);
CREATE INDEX IF NOT EXISTS idx_playback_history_started_at ON playback_history(started_at DESC);
CREATE INDEX IF NOT EXISTS idx_playback_history_track_id ON playback_history(track_id);
CREATE INDEX IF NOT EXISTS idx_playback_history_completed ON playback_history(completed);
CREATE INDEX IF NOT EXISTS idx_playback_history_track_started ON playback_history(track_id, started_at DESC);
CREATE INDEX IF NOT EXISTS idx_playback_history_path_started ON playback_history(track_path, started_at DESC);
CREATE INDEX IF NOT EXISTS idx_playback_history_media_type ON playback_history(media_type);
CREATE INDEX IF NOT EXISTS idx_playback_history_stable_key ON playback_history(stable_key);
CREATE INDEX IF NOT EXISTS idx_playback_history_stats_play_count ON playback_history_stats(play_count DESC, last_started_at DESC);
CREATE INDEX IF NOT EXISTS idx_playback_history_stats_last_started_at ON playback_history_stats(last_started_at DESC);
CREATE INDEX IF NOT EXISTS idx_playback_history_stats_media_type ON playback_history_stats(media_type);
CREATE INDEX IF NOT EXISTS idx_playback_history_stats_stable_key ON playback_history_stats(stable_key);
CREATE INDEX IF NOT EXISTS idx_playlist_items_playlist_position ON playlist_items(playlist_id, position);
CREATE INDEX IF NOT EXISTS idx_playlist_items_media ON playlist_items(media_type, media_id);
CREATE INDEX IF NOT EXISTS idx_playlist_items_source ON playlist_items(source_provider, source_item_id);
CREATE INDEX IF NOT EXISTS idx_tracks_recommendation_preference ON tracks(recommendation_preference, missing);
CREATE INDEX IF NOT EXISTS idx_tracks_skip_count ON tracks(skip_count DESC, missing);
CREATE INDEX IF NOT EXISTS idx_track_integrity_status ON track_integrity_checks(status, checked_at DESC);
CREATE INDEX IF NOT EXISTS idx_playlist_items_playlist_media ON playlist_items(playlist_id, media_type, media_id);
CREATE INDEX IF NOT EXISTS idx_playlist_items_playlist_added ON playlist_items(playlist_id, added_at DESC);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_source ON remote_tracks(source_id);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_source_availability ON remote_tracks(source_id, availability);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_server_album ON remote_tracks(
  source_id,
  provider,
  NULLIF(TRIM(COALESCE(json_extract(field_sources_json, '$.albumId'), json_extract(field_sources_json, '$.serverAlbumId'), '')), ''),
  availability
);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_title ON remote_tracks(title);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_artist ON remote_tracks(artist);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_album ON remote_tracks(album);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_genre ON remote_tracks(genre);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_sample_rate ON remote_tracks(sample_rate);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_stable_key ON remote_tracks(stable_key);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_remote_url_hash ON remote_tracks(remote_url_hash);
CREATE INDEX IF NOT EXISTS idx_remote_tracks_cover_status ON remote_tracks(cover_status);
CREATE INDEX IF NOT EXISTS idx_remote_cover_cache_cover_id ON remote_cover_cache(cover_id);
CREATE INDEX IF NOT EXISTS idx_remote_provider_scan_cache_updated ON remote_provider_scan_cache(source_id, namespace, updated_at);
CREATE INDEX IF NOT EXISTS idx_streaming_tracks_provider ON streaming_tracks(provider);
CREATE INDEX IF NOT EXISTS idx_streaming_tracks_title ON streaming_tracks(title);
CREATE INDEX IF NOT EXISTS idx_streaming_tracks_artist ON streaming_tracks(artist);
CREATE INDEX IF NOT EXISTS idx_streaming_tracks_album ON streaming_tracks(album);
${albumCandidateIndexSql}
`;
