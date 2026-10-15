/** Latest acknowledged batch per stream; separate from user-command receipts. */
export const coreMutationStreamsSql = `
  CREATE TABLE IF NOT EXISTS core_mutation_streams (
    stream_id TEXT PRIMARY KEY,
    issued_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    sequence INTEGER NOT NULL,
    request_hash TEXT NOT NULL,
    result_json TEXT NOT NULL,
    result_bytes INTEGER NOT NULL
  );
  CREATE INDEX IF NOT EXISTS core_mutation_streams_expiry ON core_mutation_streams(expires_at);
`;
