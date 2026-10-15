/** Match SQLite's existing ASCII NOCASE fallback without rescanning a folder per batch. */
export const scanPathLookupSchemaSql = `
  CREATE INDEX IF NOT EXISTS idx_tracks_active_path_nocase
    ON tracks(path COLLATE NOCASE)
    WHERE missing = 0;
`;
