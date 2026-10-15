// Album rebuilds delete old identities. Index the SET NULL foreign keys so
// SQLite does not scan every candidate for each deleted album. Null candidates
// need no index entry; this keeps the extra disk/cache footprint small.
export const albumCandidateIndexSql = `
CREATE INDEX IF NOT EXISTS idx_network_metadata_candidates_album_id
  ON network_metadata_candidates(album_id) WHERE album_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_network_cover_candidates_album_id
  ON network_cover_candidates(album_id) WHERE album_id IS NOT NULL;
`;
