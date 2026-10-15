/** Active scan working set lives on disk and is removed when its owner finishes. */
export const coreScanExecutionSchemaSql = `
CREATE TABLE IF NOT EXISTS core_scan_tasks (
  job_id TEXT PRIMARY KEY REFERENCES scan_jobs(id) ON DELETE CASCADE,
  input_json TEXT NOT NULL, scan_timestamp TEXT NOT NULL,
  sealed INTEGER NOT NULL DEFAULT 0 CHECK(sealed IN (0,1)), context_json TEXT NOT NULL DEFAULT '{}',
  file_count INTEGER NOT NULL DEFAULT 0 CHECK(file_count>=0),
  processed_count INTEGER NOT NULL DEFAULT 0 CHECK(processed_count>=0 AND processed_count<=file_count),
  skipped_count INTEGER NOT NULL DEFAULT 0 CHECK(skipped_count>=0 AND skipped_count<=processed_count),
  missing_done INTEGER NOT NULL DEFAULT 0 CHECK(missing_done IN (0,1)), missing_result INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS core_scan_files (
  job_id TEXT NOT NULL REFERENCES core_scan_tasks(job_id) ON DELETE CASCADE,
  path TEXT NOT NULL, identity TEXT NOT NULL, facts_json TEXT NOT NULL,
  track_id TEXT NOT NULL, stage TEXT NOT NULL DEFAULT 'pending' CHECK(stage IN ('pending','reading','committed','skipped','failed')),
  skipped INTEGER NOT NULL DEFAULT 0 CHECK(skipped IN (0,1)),
  PRIMARY KEY(job_id,path)
);
CREATE INDEX IF NOT EXISTS core_scan_files_pending ON core_scan_files(job_id,stage);
CREATE UNIQUE INDEX IF NOT EXISTS core_scan_files_identity ON core_scan_files(job_id,identity);
`;
