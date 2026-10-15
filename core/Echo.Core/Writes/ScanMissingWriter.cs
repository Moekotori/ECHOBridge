using Echo.Core.Database;
using Echo.Core.Protocol;

namespace Echo.Core.Writes;

/// <summary>
/// Marks tracks that a full scan did not see as <c>missing = 1</c> without loading the folder into
/// process memory. The scan streams the paths it saw into <c>core_scan_seen_paths</c> in bounded
/// batches; <see cref="Mark"/> then anti-joins inside SQLite and drops the scratch rows.
/// Case folding is ASCII-only on both sides (SQLite <c>lower()</c>), the same limit the cache
/// lookup's <c>COLLATE NOCASE</c> pass already has.
/// </summary>
internal static class ScanMissingWriter
{
    public const int MaxSeenPaths = 500;
    public const int MaxExcludeDirectories = 512;
    private const string StaleScratchSql =
        "DELETE FROM core_scan_seen_paths WHERE scan_job_id NOT IN (SELECT id FROM scan_jobs WHERE status IN ('queued','running'))";

    public static void Seen(TransactionSql sql, ScanSeenPaths seen)
    {
        if (string.IsNullOrWhiteSpace(seen.ScanJobId) || seen.Paths.Count > MaxSeenPaths
            || seen.Paths.Any(path => string.IsNullOrEmpty(path) || path.Length > ProtocolConstants.MaxScanCacheStatePathChars)
            || seen.Paths.Sum(path => (long)path.Length) > ProtocolConstants.MaxScanCacheStateBatchChars)
            throw new ProtocolLimitException("Invalid seen-path batch.");
        ScanWriteGuard.RequireActive(sql, seen.ScanJobId);
        if (sql.Value("SELECT 1 FROM core_scan_tasks WHERE job_id=$0 AND missing_done=1",seen.ScanJobId) is not null) return;
        // Prune once when this job first touches the scratch table. Running the
        // delete for every 500-path batch repeatedly scans the growing table.
        if (sql.Value("SELECT 1 FROM core_scan_seen_paths WHERE scan_job_id = $0 LIMIT 1", seen.ScanJobId) is null)
            sql.Run(StaleScratchSql);
        foreach (var path in seen.Paths)
            sql.Run("INSERT OR IGNORE INTO core_scan_seen_paths(scan_job_id, path) VALUES ($0, $1)", seen.ScanJobId, seen.CaseInsensitive ? AsciiLower(path) : path);
    }

    /// <summary>Byte-for-byte the same folding SQLite's built-in <c>lower()</c> applies (ASCII letters only).</summary>
    internal static string AsciiLower(string value)
    {
        if (!value.Any(character => character is >= 'A' and <= 'Z')) return value;
        return string.Create(value.Length, value, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++) span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
        });
    }

    public static int Mark(TransactionSql sql, ScanMarkMissing request)
    {
        if (string.IsNullOrWhiteSpace(request.ScanJobId) || string.IsNullOrWhiteSpace(request.FolderId) || string.IsNullOrWhiteSpace(request.UpdatedAt)
            || request.ExcludeDirectories.Any(directory => string.IsNullOrWhiteSpace(directory) || directory.Length > ProtocolConstants.MaxScanCacheStatePathChars))
            throw new ArgumentException("Invalid markMissing request.");
        ScanWriteGuard.RequireActive(sql, request.ScanJobId, request.FolderId);
        if (ScanExecutionCheckpoint.Exists(sql,request.ScanJobId)
            && sql.Value("SELECT scan_timestamp FROM core_scan_tasks WHERE job_id=$0",request.ScanJobId)?.ToString()!=request.UpdatedAt)
            throw new ArgumentException("Scan missing-mark timestamp changed.");
        if (sql.Value("SELECT missing_result FROM core_scan_tasks WHERE job_id=$0 AND missing_done=1",request.ScanJobId) is long previous)
            return checked((int)previous);
        if (request.ExcludeDirectories.Count > MaxExcludeDirectories)
        {
            var pagedChanged = MarkWithPagedProtection(sql, request);
            sql.Run("DELETE FROM core_scan_seen_paths WHERE scan_job_id = $0", request.ScanJobId);
            sql.Run(StaleScratchSql);
            sql.Run("UPDATE core_scan_tasks SET missing_done=1,missing_result=$1 WHERE job_id=$0",request.ScanJobId,pagedChanged);
            return pagedChanged;
        }
        var path = request.CaseInsensitive ? "lower(tracks.path)" : "tracks.path";
        var values = new List<object?> { request.UpdatedAt, request.FolderId, request.ScanJobId };
        var excluded = new System.Text.StringBuilder();
        foreach (var directory in request.ExcludeDirectories.Select(TrimSeparators).Where(directory => directory.Length > 0))
        {
            if (directory == "/")
            {
                excluded.Append($" AND substr({path}, 1, 1) <> '/'");
                continue;
            }
            var folded = request.CaseInsensitive ? AsciiLower(directory) : directory;
            // SQLite substr() counts code points, not UTF-16 units: astral characters (emoji) must count once.
            var index = values.Count;
            values.Add(folded);
            values.Add(folded + "\\");
            values.Add(folded + "/");
            excluded.Append($" AND NOT ({path} = ${index} OR substr({path}, 1, {folded.EnumerateRunes().Count() + 1}) IN (${index + 1}, ${index + 2}))");
        }
        var changed = sql.Run($"""
            UPDATE tracks SET missing = 1, updated_at = $0
            WHERE folder_id = $1 AND missing = 0
              AND NOT EXISTS (SELECT 1 FROM core_scan_seen_paths seen WHERE seen.scan_job_id = $2 AND seen.path = {path}){excluded}
            """, values.ToArray());
        sql.Run("DELETE FROM core_scan_seen_paths WHERE scan_job_id = $0", request.ScanJobId);
        sql.Run(StaleScratchSql);
        sql.Run("UPDATE core_scan_tasks SET missing_done=1,missing_result=$1 WHERE job_id=$0",request.ScanJobId,changed);
        return changed;
    }

    /** Keep protection exact when a large cloud/offline library exceeds the SQL expression budget. */
    private static int MarkWithPagedProtection(TransactionSql sql, ScanMarkMissing request)
    {
        const int pageSize = 500;
        const int updateBatchSize = 128;
        var protectedDirectories = request.ExcludeDirectories
            .Select(TrimSeparators)
            .Where(directory => directory.Length > 0)
            .Select(directory => request.CaseInsensitive ? AsciiLower(directory) : directory)
            .ToHashSet(StringComparer.Ordinal);
        var path = request.CaseInsensitive ? "lower(tracks.path)" : "tracks.path";
        long cursor = 0;
        var changed = 0;
        while (true)
        {
            var page = new List<(long RowId, string Id, string Path)>();
            using (var command = sql.Command($"""
                SELECT tracks.rowid, tracks.id, tracks.path FROM tracks
                WHERE tracks.folder_id = $0 AND tracks.missing = 0 AND tracks.rowid > $1
                  AND NOT EXISTS (SELECT 1 FROM core_scan_seen_paths seen WHERE seen.scan_job_id = $2 AND seen.path = {path})
                ORDER BY tracks.rowid LIMIT $3
                """, request.FolderId, cursor, request.ScanJobId, pageSize))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) page.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            if (page.Count == 0) break;
            cursor = page[^1].RowId;
            var missingIds = page.Where(row => !IsInsideProtectedDirectory(row.Path, protectedDirectories, request.CaseInsensitive))
                .Select(row => row.Id).ToArray();
            for (var offset = 0; offset < missingIds.Length; offset += updateBatchSize)
            {
                var batch = missingIds.Skip(offset).Take(updateBatchSize).ToArray();
                var placeholders = string.Join(',', batch.Select((_, index) => $"${index + 1}"));
                changed += sql.Run($"UPDATE tracks SET missing = 1, updated_at = $0 WHERE id IN ({placeholders})",
                    [request.UpdatedAt, .. batch]);
            }
        }
        return changed;
    }

    private static bool IsInsideProtectedDirectory(string path, HashSet<string> directories, bool caseInsensitive)
    {
        if (path.StartsWith('/') && directories.Contains("/")) return true;
        var current = caseInsensitive ? AsciiLower(path) : path;
        while (current.Length > 0)
        {
            if (directories.Contains(current)) return true;
            var separator = current.LastIndexOfAny(['\\', '/']);
            if (separator < 0) break;
            current = current[..separator];
        }
        return false;
    }

    private static string TrimSeparators(string value)
    {
        var trimmed = value.TrimEnd('\\', '/');
        return trimmed.Length == 0 && value.StartsWith('/') ? "/" : trimmed;
    }
}
