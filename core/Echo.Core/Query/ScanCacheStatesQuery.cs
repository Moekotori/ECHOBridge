using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

/// <summary>
/// Scan-time cache lookup for one folder. Replaces the Electron main-thread
/// <c>getTrackCacheStatesByPaths</c> / <c>getTrackCacheStatesByFolder</c> SQL; the row shape is
/// identical so the caller keeps its existing state mapper. Never hydrates full tracks.
/// </summary>
public static class ScanCacheStatesQuery
{
    public const string Capability = "scan.cacheStates/v1";

    private const string SelectSql = """
        SELECT
          tracks.rowid, tracks.path, tracks.id, tracks.size_bytes, tracks.mtime_ms, tracks.duration,
          tracks.title, tracks.artist, tracks.album, tracks.album_artist,
          tracks.track_no, tracks.disc_no, tracks.year, tracks.genre,
          tracks.codec, tracks.sample_rate, tracks.bit_depth, tracks.bitrate, tracks.bpm,
          tracks.replay_gain_track_gain_db, tracks.replay_gain_album_gain_db,
          tracks.replay_gain_track_peak, tracks.replay_gain_album_peak, tracks.replay_gain_integrated_lufs,
          tracks.cover_id, tracks.metadata_status, tracks.embedded_metadata_status, tracks.embedded_cover_status,
          tracks.field_sources_json,
          tracks.file_identity, tracks.file_identity_source, tracks.quick_hash, tracks.quick_hash_version,
          tracks.identity_status, tracks.identity_updated_at, tracks.identity_error,
          covers.source_type, covers.source_hash, covers.mime_type,
          covers.thumb_path, covers.album_path, covers.large_path, covers.original_ref,
          covers.cache_version
        """;

    internal static string PathBatchSql(int count, bool caseInsensitive)
    {
        var placeholders = string.Join(',', Enumerable.Repeat("?", count));
        var comparison = caseInsensitive ? "tracks.path COLLATE NOCASE" : "tracks.path";
        // Both collations need a matching path index to avoid a folder scan per batch.
        var table = caseInsensitive ? "tracks INDEXED BY idx_tracks_active_path_nocase" : "tracks INDEXED BY idx_tracks_path";
        return $"{SelectSql}\nFROM {table} LEFT JOIN covers ON covers.id = tracks.cover_id\nWHERE tracks.folder_id = ? AND tracks.missing = 0 AND {comparison} IN ({placeholders})";
    }

    public static ScanCacheStatesPage Execute(ReadOnlyLibraryDatabase database, ScanCacheStatesParams? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.FolderId) || request.FolderId.Length > 512)
            throw new ArgumentException("scan cache lookup requires folderId.");
        return request.Paths is not null ? ByPaths(database, request) : ByFolder(database, request);
    }

    private static ScanCacheStatesPage ByPaths(ReadOnlyLibraryDatabase database, ScanCacheStatesParams request)
    {
        var paths = request.Paths!;
        if (paths.Count > ProtocolConstants.MaxScanCacheStatePaths
            || paths.Any(path => string.IsNullOrEmpty(path) || path.Length > ProtocolConstants.MaxScanCacheStatePathChars)
            || paths.Sum(path => (long)path.Length) > ProtocolConstants.MaxScanCacheStateBatchChars)
            throw new ProtocolLimitException("Invalid scan cache path batch.");
        var page = new ScanCacheStatesPage();
        if (paths.Count == 0) return page;
        var unique = paths.Distinct(StringComparer.Ordinal).ToArray();
        using var reader = CachedQuery.Reader(database,
            PathBatchSql(unique.Length, request.CaseInsensitive),
            command => { command.Add(request.FolderId); foreach (var path in unique) command.Add(path); });
        while (reader.Read()) page.Items.Add(Map(reader));
        return page;
    }

    private static ScanCacheStatesPage ByFolder(ReadOnlyLibraryDatabase database, ScanCacheStatesParams request)
    {
        var limit = Math.Clamp(request.Limit ?? ProtocolConstants.MaxScanCacheStatePaths, 1, ProtocolConstants.MaxScanCacheStatePaths);
        var cursor = request.Cursor ?? 0;
        var page = new ScanCacheStatesPage { Cursor = cursor };
        using var reader = CachedQuery.Reader(database,
            $"{SelectSql}\nFROM tracks LEFT JOIN covers ON covers.id = tracks.cover_id\nWHERE tracks.folder_id = ? AND tracks.missing = 0 AND tracks.rowid > ? ORDER BY tracks.rowid LIMIT ?",
            command => { command.Add(request.FolderId); command.Add(cursor); command.Add(limit + 1); });
        while (reader.Read())
        {
            if (page.Items.Count == limit) { page.HasMore = true; break; }
            var row = Map(reader);
            page.Items.Add(row);
            page.Cursor = row.RowId;
        }
        return page;
    }

    private static ScanCacheStateRowDto Map(SqliteDataReader reader) => new()
    {
        RowId = reader.GetInt64(0),
        Path = reader.GetString(1),
        Id = reader.GetString(2),
        SizeBytes = Num(reader, 3), MtimeMs = Num(reader, 4), Duration = Num(reader, 5),
        Title = Text(reader, 6), Artist = Text(reader, 7), Album = Text(reader, 8), AlbumArtist = Text(reader, 9),
        TrackNo = Num(reader, 10), DiscNo = Num(reader, 11), Year = Num(reader, 12), Genre = Text(reader, 13),
        Codec = Text(reader, 14), SampleRate = Num(reader, 15), BitDepth = Num(reader, 16), Bitrate = Num(reader, 17), Bpm = Num(reader, 18),
        ReplayGainTrackGainDb = Num(reader, 19), ReplayGainAlbumGainDb = Num(reader, 20),
        ReplayGainTrackPeak = Num(reader, 21), ReplayGainAlbumPeak = Num(reader, 22), ReplayGainIntegratedLufs = Num(reader, 23),
        CoverId = Text(reader, 24), MetadataStatus = Text(reader, 25), EmbeddedMetadataStatus = Text(reader, 26), EmbeddedCoverStatus = Text(reader, 27),
        FieldSourcesJson = Text(reader, 28),
        FileIdentity = Text(reader, 29), FileIdentitySource = Text(reader, 30), QuickHash = Text(reader, 31), QuickHashVersion = Num(reader, 32),
        IdentityStatus = Text(reader, 33), IdentityUpdatedAt = Text(reader, 34), IdentityError = Text(reader, 35),
        SourceType = Text(reader, 36), SourceHash = Text(reader, 37), MimeType = Text(reader, 38),
        ThumbPath = Text(reader, 39), AlbumPath = Text(reader, 40), LargePath = Text(reader, 41), OriginalRef = Text(reader, 42),
        CacheVersion = Num(reader, 43),
    };

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal) switch { string text => text, var other => Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture) };

    private static double? Num(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        return reader.GetValue(ordinal) switch
        {
            long value => value,
            double value => value,
            string text when double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
