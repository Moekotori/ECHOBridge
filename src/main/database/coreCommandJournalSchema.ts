/** Additive metadata only; business data and existing migration ownership stay unchanged. */
export const coreCommandJournalSql = `
  CREATE TABLE IF NOT EXISTS core_command_journal (
    operation_id TEXT PRIMARY KEY,
    request_hash TEXT NOT NULL,
    expires_at INTEGER NOT NULL,
    result_json TEXT NOT NULL,
    result_bytes INTEGER NOT NULL
  );
  CREATE INDEX IF NOT EXISTS core_command_journal_expiry ON core_command_journal(expires_at);
`;
