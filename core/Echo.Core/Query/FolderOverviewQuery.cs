using System.Text.Json;
using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class FolderOverviewQuery
{
    private const string FarFuture = "9999-12-31T23:59:59.999Z";

    public static List<LibraryFolderOverviewDto> Execute(ReadOnlyLibraryDatabase database)
    {
        using var transaction = database.BeginDeferredRead();
        var folders = ListFolders(database);
        if (folders.Count == 0)
        {
            return folders;
        }

        var stats = ListTrackStats(database);
        var albums = ListCounts(database, """
            SELECT tracks.folder_id, COUNT(DISTINCT album_tracks.album_id)
            FROM tracks
            INNER JOIN album_tracks ON album_tracks.track_id = tracks.id
            WHERE tracks.missing = 0 AND tracks.library_hidden = 0
            GROUP BY tracks.folder_id
            """);
        var artists = ListCounts(database, """
            SELECT tracks.folder_id, COUNT(DISTINCT artist_tracks.artist_id)
            FROM tracks
            INNER JOIN artist_tracks ON artist_tracks.track_id = tracks.id
            WHERE tracks.missing = 0 AND tracks.library_hidden = 0
            GROUP BY tracks.folder_id
            """);
        var scans = ListLatestScans(database);
        ApplyPathDerivedFields(database, folders);

        foreach (var folder in folders)
        {
            if (stats.TryGetValue(folder.Id, out var row))
            {
                folder.TrackCount = row.TrackCount;
                folder.TotalDuration = row.TotalDuration;
                folder.TotalSizeBytes = row.TotalSizeBytes;
                folder.LosslessTrackCount = row.LosslessCount;
                folder.HiResTrackCount = row.HiResCount;
                folder.UnplayedTrackCount = row.UnplayedCount;
                folder.MissingCoverTrackCount = row.MissingCoverCount;
                folder.MissingLyricsTrackCount = row.MissingLyricsCount;
                folder.DuplicateTrackCount = row.DuplicateCount;
                folder.RecentlyAddedTrackCount = row.RecentlyAddedCount;
                folder.MissingTrackCount = row.MissingCount;
            }

            folder.AlbumCount = albums.GetValueOrDefault(folder.Id);
            folder.ArtistCount = artists.GetValueOrDefault(folder.Id);
            folder.RecentScan = scans.GetValueOrDefault(folder.Id);
        }

        return folders;
    }

    private static List<LibraryFolderOverviewDto> ListFolders(ReadOnlyLibraryDatabase database)
    {
        const string sql = """
            SELECT id, path, name, status, enabled, import_profile, last_scan_at, created_at, updated_at
            FROM folders
            WHERE enabled = 1 AND status != 'removed'
            ORDER BY path COLLATE NOCASE
            """;
        var items = new List<LibraryFolderOverviewDto>();
        using var reader = CachedQuery.Reader(database, sql);
        while (reader.Read())
        {
            var enabled = reader.IsDBNull(4) ? 1 : Convert.ToInt32(reader.GetValue(4));
            var status = Text(reader, 3);
            items.Add(new LibraryFolderOverviewDto
            {
                Id = Text(reader, 0) ?? string.Empty,
                Path = Text(reader, 1) ?? string.Empty,
                Name = Text(reader, 2) ?? string.Empty,
                Status = enabled == 0 || status == "removed" ? "removed" : "active",
                ImportProfile = Text(reader, 5) == "osu" ? "osu" : "normal",
                LastScanAt = Text(reader, 6),
                CreatedAt = Text(reader, 7) ?? string.Empty,
                UpdatedAt = Text(reader, 8) ?? string.Empty,
            });
        }

        return items;
    }

    private static Dictionary<string, FolderTrackStats> ListTrackStats(ReadOnlyLibraryDatabase database)
    {
        const string sql = """
            WITH latest_completed AS (
              SELECT folder_id, started_at
              FROM (
                SELECT folder_id, started_at,
                  ROW_NUMBER() OVER (
                    PARTITION BY folder_id
                    ORDER BY COALESCE(finished_at, updated_at, created_at) DESC
                  ) AS rn
                FROM scan_jobs
                WHERE status = 'completed' AND started_at IS NOT NULL
              )
              WHERE rn = 1
            )
            SELECT
              tracks.folder_id,
              SUM(CASE WHEN tracks.missing = 0 AND tracks.library_hidden = 0 THEN 1 ELSE 0 END) AS track_count,
              SUM(CASE WHEN tracks.missing = 0 AND tracks.library_hidden = 0 THEN COALESCE(tracks.duration, 0) ELSE 0 END) AS total_duration,
              SUM(CASE WHEN tracks.missing = 0 AND tracks.library_hidden = 0 THEN COALESCE(tracks.size_bytes, 0) ELSE 0 END) AS total_size_bytes,
              SUM(CASE WHEN tracks.missing = 0 AND tracks.library_hidden = 0 AND UPPER(COALESCE(tracks.codec, '')) IN ('FLAC', 'ALAC', 'WAV', 'AIFF', 'APE', 'DSF', 'DFF') THEN 1 ELSE 0 END) AS lossless_count,
              SUM(CASE WHEN tracks.missing = 0 AND tracks.library_hidden = 0 AND ((COALESCE(tracks.bit_depth, 0) >= 24 AND COALESCE(tracks.sample_rate, 0) >= 88200) OR UPPER(COALESCE(tracks.codec, '')) IN ('DSF', 'DFF', 'DSD')) THEN 1 ELSE 0 END) AS hires_count,
              SUM(CASE WHEN tracks.missing = 0 AND tracks.library_hidden = 0 AND COALESCE(tracks.play_count, 0) = 0 THEN 1 ELSE 0 END) AS unplayed_count,
              SUM(CASE WHEN tracks.missing = 0 AND tracks.library_hidden = 0 AND NOT EXISTS (
                SELECT 1 FROM covers
                WHERE covers.id = tracks.cover_id
                  AND covers.source_type != 'default'
              ) THEN 1 ELSE 0 END) AS missing_cover_count,
              SUM(CASE WHEN tracks.missing = 0 AND tracks.library_hidden = 0 AND NOT EXISTS (
                SELECT 1 FROM lyrics_cache
                WHERE lyrics_cache.track_id = tracks.id
                  AND lyrics_cache.kind IN ('plain', 'synced', 'instrumental')
              ) THEN 1 ELSE 0 END) AS missing_lyrics_count,
              SUM(CASE WHEN tracks.missing = 0 AND tracks.library_hidden = 0 AND EXISTS (
                SELECT 1
                FROM duplicate_track_members
                INNER JOIN duplicate_track_groups ON duplicate_track_groups.id = duplicate_track_members.group_id
                WHERE duplicate_track_members.track_id = tracks.id
                  AND duplicate_track_groups.mode = 'strict'
              ) THEN 1 ELSE 0 END) AS duplicate_count,
              SUM(CASE WHEN tracks.missing = 0 AND tracks.library_hidden = 0 AND tracks.created_at >= COALESCE(latest_completed.started_at, ?) THEN 1 ELSE 0 END) AS recently_added_count,
              SUM(CASE WHEN tracks.missing != 0 THEN 1 ELSE 0 END) AS missing_count
            FROM tracks
            LEFT JOIN latest_completed ON latest_completed.folder_id = tracks.folder_id
            GROUP BY tracks.folder_id
            """;
        var items = new Dictionary<string, FolderTrackStats>(StringComparer.Ordinal);
        using var reader = CachedQuery.Reader(database, sql, command => command.Add(FarFuture));
        while (reader.Read())
        {
            var folderId = Text(reader, 0);
            if (string.IsNullOrEmpty(folderId))
            {
                continue;
            }

            items[folderId] = new FolderTrackStats(
                Int(reader, 1),
                Real(reader, 2),
                Long(reader, 3),
                Int(reader, 4),
                Int(reader, 5),
                Int(reader, 6),
                Int(reader, 7),
                Int(reader, 8),
                Int(reader, 9),
                Int(reader, 10),
                Int(reader, 11));
        }

        return items;
    }

    private static Dictionary<string, int> ListCounts(ReadOnlyLibraryDatabase database, string sql)
    {
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        using var reader = CachedQuery.Reader(database, sql);
        while (reader.Read())
        {
            var folderId = Text(reader, 0);
            if (!string.IsNullOrEmpty(folderId))
            {
                items[folderId] = Int(reader, 1);
            }
        }

        return items;
    }

    private static Dictionary<string, LibraryScanStatusDto> ListLatestScans(ReadOnlyLibraryDatabase database)
    {
        const string sql = """
            SELECT
              id, folder_id, status, phase,
              discovered_count, parsed_count, skipped_count, cover_count,
              total_files, processed_files, skipped_files,
              added_tracks, updated_tracks, removed_tracks,
              error_count, errors_json, started_at, finished_at
            FROM (
              SELECT
                id, folder_id, status, phase,
                discovered_count, parsed_count, skipped_count, cover_count,
                total_files, processed_files, skipped_files,
                added_tracks, updated_tracks, removed_tracks,
                error_count, errors_json, started_at, finished_at,
                ROW_NUMBER() OVER (PARTITION BY folder_id ORDER BY created_at DESC) AS rn
              FROM scan_jobs
            )
            WHERE rn = 1
            """;
        var items = new Dictionary<string, LibraryScanStatusDto>(StringComparer.Ordinal);
        using var reader = CachedQuery.Reader(database, sql);
        while (reader.Read())
        {
            var folderId = Text(reader, 1) ?? string.Empty;
            items[folderId] = new LibraryScanStatusDto
            {
                Id = Text(reader, 0) ?? string.Empty,
                FolderId = folderId,
                Status = MapScanStatus(Text(reader, 2)),
                Phase = MapScanPhase(Text(reader, 3)),
                TotalFiles = CoalesceInt(reader, 4, 8),
                ProcessedFiles = CoalesceInt(reader, 5, 9),
                SkippedFiles = CoalesceInt(reader, 6, 10),
                CoverCount = Int(reader, 7),
                AddedTracks = Int(reader, 11),
                UpdatedTracks = Int(reader, 12),
                RemovedTracks = Int(reader, 13),
                ErrorCount = Int(reader, 14),
                Errors = ParseErrors(Text(reader, 15)),
                StartedAt = Text(reader, 16),
                FinishedAt = Text(reader, 17),
            };
        }

        return items;
    }

    private static void ApplyPathDerivedFields(ReadOnlyLibraryDatabase database, List<LibraryFolderOverviewDto> folders)
    {
        var byId = folders.ToDictionary(folder => folder.Id, StringComparer.Ordinal);
        var childNames = folders.ToDictionary(
            folder => folder.Id,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var covers = folders.ToDictionary(
            folder => folder.Id,
            _ => new FolderCoverSelection(),
            StringComparer.Ordinal);

        const string sql = """
            SELECT tracks.folder_id, tracks.path, tracks.cover_id, tracks.updated_at, covers.source_type
            FROM tracks
            LEFT JOIN covers ON covers.id = tracks.cover_id
            WHERE tracks.missing = 0 AND tracks.library_hidden = 0
            """;
        using var reader = CachedQuery.Reader(database, sql);
        while (reader.Read())
        {
            var folderId = Text(reader, 0);
            if (string.IsNullOrEmpty(folderId) || !byId.TryGetValue(folderId, out var folder))
            {
                continue;
            }

            var trackPath = Text(reader, 1);
            if (string.IsNullOrEmpty(trackPath) || !TryRelativePath(folder.Path, trackPath, out var relative))
            {
                continue;
            }

            var parts = relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                childNames[folderId].Add(parts[0]);
            }

            var coverId = Text(reader, 2);
            var coverSource = Text(reader, 4);
            if (!string.IsNullOrEmpty(coverId) && !string.Equals(coverSource, "default", StringComparison.Ordinal))
            {
                covers[folderId].Add(coverId, Text(reader, 3) ?? string.Empty);
            }
        }

        foreach (var folder in folders)
        {
            folder.ChildFolderCount = childNames[folder.Id].Count;
            folder.CoverThumbs = covers[folder.Id].CoverIds
                .Select(coverId => LibraryText.CoverUrl(coverId, "album"))
                .Where(static url => url.Length > 0)
                .ToList();
        }
    }

    private static bool TryRelativePath(string folderPath, string trackPath, out string relative)
    {
        var prefix = StripTrailingSeparators(folderPath);
        if (trackPath.StartsWith(prefix, StringComparison.Ordinal)
            && trackPath.Length > prefix.Length
            && (trackPath[prefix.Length] is '/' or '\\'))
        {
            relative = trackPath[(prefix.Length + 1)..];
            return true;
        }

        relative = string.Empty;
        return false;
    }

    private static string StripTrailingSeparators(string path)
    {
        var end = path.Length;
        while (end > 1 && (path[end - 1] is '/' or '\\'))
        {
            end -= 1;
        }

        return end == path.Length ? path : path[..end];
    }

    private static string MapScanStatus(string? value) =>
        value is "queued" or "running" or "completed" or "cancelled" or "failed" ? value : "failed";

    private static string MapScanPhase(string? value) =>
        value is "queued"
            or "discovering"
            or "checking_cache"
            or "reading_metadata"
            or "extracting_covers"
            or "grouping_albums"
            or "writing_database"
            or "finished"
            or "failed"
            or "cancelled"
            ? value
            : "queued";

    private static List<string> ParseErrors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item!)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int CoalesceInt(Microsoft.Data.Sqlite.SqliteDataReader reader, int preferred, int fallback) =>
        reader.IsDBNull(preferred) ? Int(reader, fallback) : Convert.ToInt32(reader.GetValue(preferred));

    private static string? Text(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static int Int(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static long Long(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal));

    private static double Real(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToDouble(reader.GetValue(ordinal));

    private readonly record struct FolderTrackStats(
        int TrackCount,
        double TotalDuration,
        long TotalSizeBytes,
        int LosslessCount,
        int HiResCount,
        int UnplayedCount,
        int MissingCoverCount,
        int MissingLyricsCount,
        int DuplicateCount,
        int RecentlyAddedCount,
        int MissingCount);
}
