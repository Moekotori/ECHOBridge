/**
 * Scratch rows for the Core `markMissing` mutation: the paths one running scan job has seen.
 * Filled in bounded batches, consumed and deleted by the final anti-join; rows of jobs that are no
 * longer queued/running are pruned on every touch so a failed scan cannot leak them.
 */
export const coreScanSeenPathsSql = `
  CREATE TABLE IF NOT EXISTS core_scan_seen_paths (
    scan_job_id TEXT NOT NULL,
    path TEXT NOT NULL,
    PRIMARY KEY (scan_job_id, path)
  ) WITHOUT ROWID;
`;
