/** One small disk-backed dispatch owner per active durable scan; no media results. */
export const coreScanDispatchSchemaSql = `
CREATE TABLE IF NOT EXISTS core_scan_dispatch (
  job_id TEXT PRIMARY KEY REFERENCES core_scan_tasks(job_id) ON DELETE CASCADE,
  owner TEXT NOT NULL,
  cursor INTEGER NOT NULL DEFAULT 0 CHECK(cursor>=0),
  pass INTEGER NOT NULL DEFAULT 0 CHECK(pass BETWEEN 0 AND 2),
  page_end INTEGER NOT NULL DEFAULT 0 CHECK(page_end>=cursor),
  exhausted INTEGER NOT NULL DEFAULT 0 CHECK(exhausted IN (0,1))
);
`;
