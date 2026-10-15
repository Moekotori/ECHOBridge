using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

/// <summary>Classifies a bounded scan batch using small fingerprints; hydrates full cache rows only for changes.</summary>
public static class ScanChangedFilesQuery
{
    public const string Capability = "scan.changedFiles/v2";
    public const string LegacyCapability = "scan.changedFiles/v1";
    public const string CueRevisionCapability = "scan.cueRevision/v2";
    private const int MaxFiles = 400;

    private sealed record FingerprintRow(string Path, double? SizeBytes, double? MtimeMs, bool NeedsMetadata, string? CueRevisionMarker);

    public static ScanChangedFilesResult Execute(ReadOnlyLibraryDatabase database, ScanChangedFilesParams? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.FolderId) || request.FolderId.Length > 512)
            throw new ArgumentException("Scan change lookup requires folderId.");
        var files = request.Files;
        if (files is null || files.Count > MaxFiles
            || files.Any(file => file is null || string.IsNullOrEmpty(file.Path)
                || file.Path.Length > ProtocolConstants.MaxScanCacheStatePathChars
                || !double.IsFinite(file.SizeBytes) || file.SizeBytes < 0
                || !double.IsFinite(file.MtimeMs) || file.MtimeMs < 0
                || file.CueRevision is not null && (file.CueRevision.Length != 16 || file.CueRevision.Any(c => !char.IsAsciiHexDigit(c))))
            || files.Sum(file => (long)file.Path.Length) > ProtocolConstants.MaxScanCacheStateBatchChars)
            throw new ProtocolLimitException("Invalid scan fingerprint batch.");

        var result = new ScanChangedFilesResult();
        if (files.Count == 0) return result;
        var paths = files.Select(file => file.Path).Distinct(StringComparer.Ordinal).ToArray();
        var exact = ReadFingerprints(database, request.FolderId, paths, false);
        var rowsByPath = exact.ToDictionary(row => row.Path, StringComparer.Ordinal);

        if (request.CaseInsensitive)
        {
            var unmatched = paths.Where(path => !rowsByPath.ContainsKey(path)).ToArray();
            if (unmatched.Length > 0)
            {
                var matchedKeys = new HashSet<string>(rowsByPath.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (var row in ReadFingerprints(database, request.FolderId, unmatched, true))
                    if (matchedKeys.Add(row.Path)) rowsByPath[row.Path] = row;
            }
        }

        var compare = request.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var byComparePath = new Dictionary<string, FingerprintRow>(compare);
        foreach (var row in rowsByPath.Values)
            byComparePath.TryAdd(row.Path, row);

        var changed = new List<(int Index, FingerprintRow? Row)>();
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            byComparePath.TryGetValue(file.Path, out var row);
            if (row is not null && row.SizeBytes == file.SizeBytes && row.MtimeMs == file.MtimeMs && !row.NeedsMetadata
                && (file.CueRevision is null || row.CueRevisionMarker == "cueRevision:" + file.CueRevision)) continue;
            changed.Add((index, row));
        }

        var changedPaths = changed.Where(item => item.Row is not null).Select(item => item.Row!.Path).Distinct(StringComparer.Ordinal).ToList();
        var states = changedPaths.Count == 0 ? new Dictionary<string, ScanCacheStateRowDto>(StringComparer.Ordinal)
            : ScanCacheStatesQuery.Execute(database, new ScanCacheStatesParams { FolderId = request.FolderId, Paths = changedPaths })
                .Items.ToDictionary(row => row.Path, StringComparer.Ordinal);
        foreach (var (index, row) in changed)
            result.Items.Add(new ScanChangedFileDto { Index = index, State = row is not null && states.TryGetValue(row.Path, out var state) ? state : null });
        return result;
    }

    internal static string FingerprintBatchSql(int count, bool caseInsensitive)
    {
        var placeholders = string.Join(',', Enumerable.Repeat("?", count));
        var comparison = caseInsensitive ? "tracks.path COLLATE NOCASE" : "tracks.path";
        // Both collations need a matching path index to avoid a folder scan per batch.
        var table = caseInsensitive ? "tracks INDEXED BY idx_tracks_active_path_nocase" : "tracks INDEXED BY idx_tracks_path";
        return $"""
            SELECT tracks.path, tracks.size_bytes, tracks.mtime_ms,
              CASE WHEN tracks.embedded_metadata_status IN ('pending','reading','error')
                OR tracks.metadata_status IN ('fallback','error') THEN 1 ELSE 0 END,
              CASE WHEN tracks.path LIKE '%#cueTrack=%' THEN (
                SELECT key FROM json_each(CASE WHEN json_valid(tracks.field_sources_json) THEN tracks.field_sources_json ELSE json_object() END)
                WHERE key GLOB 'cueRevision:*' AND value = 'technical' LIMIT 1
              ) ELSE NULL END
            FROM {table} WHERE tracks.folder_id = ? AND tracks.missing = 0 AND {comparison} IN ({placeholders})
            """;
    }

    private static List<FingerprintRow> ReadFingerprints(ReadOnlyLibraryDatabase database, string folderId, string[] paths, bool caseInsensitive)
    {
        if (paths.Length == 0) return [];
        using var reader = CachedQuery.Reader(database, FingerprintBatchSql(paths.Length, caseInsensitive),
            command => { command.Add(folderId); foreach (var path in paths) command.Add(path); });
        var rows = new List<FingerprintRow>();
        while (reader.Read())
            rows.Add(new FingerprintRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDouble(1), reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.GetInt32(3) == 1, reader.IsDBNull(4) ? null : reader.GetString(4)));
        return rows;
    }
}
