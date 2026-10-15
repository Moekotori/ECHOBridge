using System.Text.Json;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class QualityIssueQuery
{
    private static readonly string[] Kinds =
        ["missing_cover", "missing_lyrics", "fallback_metadata", "unknown_artist_album", "embedded_read_failed"];

    private sealed record Evidence(
        string Id,
        string? CoverId,
        string? CoverSource,
        string? MetadataStatus,
        string? EmbeddedMetadataStatus,
        string? EmbeddedCoverStatus,
        string Artist,
        string Album,
        string AlbumArtist,
        string? FieldSourcesJson,
        int CandidateCount);

    public static Dictionary<string, int> Overview(ReadOnlyLibraryDatabase database)
    {
        var counts = new Dictionary<string, int>(Kinds.Length, StringComparer.Ordinal);
        using var transaction = database.BeginDeferredRead();
        foreach (var kind in Kinds)
        {
            var sql = $"SELECT COUNT(*) FROM tracks WHERE tracks.missing = 0 AND tracks.library_hidden = 0 AND ({Condition(kind)})";
            counts.Add(kind, Convert.ToInt32(CachedQuery.Scalar(database, sql) ?? 0));
        }
        return counts;
    }

    public static CoreQualityIssuePage Issues(ReadOnlyLibraryDatabase database, CoreQualityIssueQuery query)
    {
        if (query.SourceProvider is not null and not "local")
            throw new UnsupportedQueryException("Quality issues support local tracks only.");
        var condition = Condition(query.Kind);
        var page = Math.Max(1, query.Page ?? 1);
        var pageSize = Math.Clamp(query.PageSize ?? 50, 1, 100);
        var offset = checked(((long)page - 1) * pageSize);
        var hasSearch = !string.IsNullOrWhiteSpace(query.FtsMatch);
        var join = hasSearch ? "INNER JOIN tracks_fts ON tracks_fts.rowid = tracks.rowid" : string.Empty;
        var where = $"WHERE tracks.missing = 0 AND tracks.library_hidden = 0 AND ({condition}){(hasSearch ? " AND tracks_fts MATCH ?" : string.Empty)}";
        var countSql = $"SELECT COUNT(*) FROM tracks {join} {where}";
        var pageSql = $"""
            SELECT tracks.id, tracks.cover_id, covers.source_type,
                   tracks.metadata_status, tracks.embedded_metadata_status, tracks.embedded_cover_status,
                   tracks.artist, tracks.album, tracks.album_artist, tracks.field_sources_json,
                   (SELECT COUNT(*) FROM network_metadata_candidates WHERE track_id = tracks.id)
                   + (SELECT COUNT(*) FROM network_cover_candidates WHERE track_id = tracks.id)
            FROM tracks
            LEFT JOIN covers ON covers.id = tracks.cover_id
            {join}
            {where}
            ORDER BY tracks.updated_at DESC, tracks.title COLLATE NOCASE
            LIMIT ? OFFSET ?
            """;

        using var transaction = database.BeginDeferredRead();
        var total = Convert.ToInt32(CachedQuery.Scalar(database, countSql, command =>
        {
            if (hasSearch) command.Add(query.FtsMatch);
        }) ?? 0);
        var ids = new List<string>(Math.Min(total, pageSize));
        var evidence = new Dictionary<string, Evidence>(StringComparer.Ordinal);
        using (var reader = CachedQuery.Reader(database, pageSql, command =>
        {
            if (hasSearch) command.Add(query.FtsMatch);
            command.Add(pageSize);
            command.Add(offset);
        }))
        {
            while (reader.Read())
            {
                var row = new Evidence(
                    reader.GetString(0), Text(reader, 1), Text(reader, 2), Text(reader, 3),
                    Text(reader, 4), Text(reader, 5), Text(reader, 6) ?? string.Empty,
                    Text(reader, 7) ?? string.Empty, Text(reader, 8) ?? string.Empty,
                    Text(reader, 9), reader.GetInt32(10));
                ids.Add(row.Id);
                evidence.Add(row.Id, row);
            }
        }

        var items = new List<CoreQualityIssueItem>(ids.Count);
        foreach (var track in TrackByIdQuery.Execute(database, ids))
        {
            var row = evidence[track.Id];
            items.Add(new CoreQualityIssueItem
            {
                Track = track,
                Reasons = Reasons(query.Kind, row),
                CandidateCount = row.CandidateCount,
            });
        }
        return new CoreQualityIssuePage
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total,
            HasMore = offset + items.Count < total,
            Kind = query.Kind,
        };
    }

    private static string Condition(string kind) => kind switch
    {
        "missing_cover" => "NOT EXISTS (SELECT 1 FROM covers WHERE covers.id = tracks.cover_id AND covers.source_type != 'default')",
        "missing_lyrics" => "NOT EXISTS (SELECT 1 FROM lyrics_cache WHERE lyrics_cache.track_id = tracks.id AND lyrics_cache.kind IN ('plain', 'synced', 'instrumental'))",
        "fallback_metadata" => "tracks.metadata_status = 'fallback'",
        "unknown_artist_album" => "(lower(trim(tracks.artist)) IN ('', 'unknown', 'unknown artist') OR lower(trim(tracks.album)) IN ('', 'unknown', 'unknown album') OR lower(trim(tracks.album_artist)) IN ('', 'unknown', 'unknown artist', 'unknown album'))",
        "embedded_read_failed" => "tracks.embedded_metadata_status = 'error' OR tracks.embedded_cover_status = 'error'",
        _ => throw new UnsupportedQueryException("Unsupported library quality issue kind."),
    };

    private static List<string> Reasons(string kind, Evidence row)
    {
        var reasons = new List<string>(3);
        if (kind == "missing_cover" && (string.IsNullOrWhiteSpace(row.CoverId)
            || string.IsNullOrWhiteSpace(row.CoverSource) || row.CoverSource == "default")) reasons.Add("missing_cover");
        if (kind == "missing_lyrics") reasons.Add("missing_lyrics");
        if (kind == "fallback_metadata")
        {
            if (row.MetadataStatus == "fallback") reasons.Add("metadata_fallback");
            if (HasFilenameFallback(row.FieldSourcesJson)) reasons.Add("filename_fallback");
            reasons.AddRange(ReadFailureReasons(row.FieldSourcesJson));
        }
        if (kind == "unknown_artist_album")
        {
            var artist = row.Artist.Trim().ToLowerInvariant();
            var album = row.Album.Trim().ToLowerInvariant();
            var albumArtist = row.AlbumArtist.Trim().ToLowerInvariant();
            if (artist is "" or "unknown" or "unknown artist") reasons.Add("unknown_artist");
            if (album.Length == 0) reasons.Add("missing_album");
            else if (album is "unknown" or "unknown album") reasons.Add("unknown_album");
            if (albumArtist is "" or "unknown" or "unknown artist" or "unknown album") reasons.Add("missing_album_artist");
        }
        if (kind == "embedded_read_failed")
        {
            if (row.EmbeddedMetadataStatus == "error") reasons.Add("embedded_metadata_error");
            if (row.EmbeddedCoverStatus == "error") reasons.Add("embedded_cover_error");
            reasons.AddRange(ReadFailureReasons(row.FieldSourcesJson));
        }
        return reasons;
    }

    private const string ReadFailureMarkerPrefix = "readFailure:";
    private static readonly HashSet<string> ReadFailureCodes = new(StringComparer.Ordinal)
    {
        "timeout", "access_denied", "file_missing", "file_locked",
        "unsupported_format", "corrupt_file", "cloud_unavailable", "unknown",
    };

    /// <summary>Why the last scan could not read this file's tags, from the marker the scanner stores in field sources.</summary>
    internal static List<string> ReadFailureReasons(string? fieldSourcesJson)
    {
        var reasons = new List<string>(1);
        if (string.IsNullOrWhiteSpace(fieldSourcesJson)) return reasons;
        try
        {
            using var document = JsonDocument.Parse(fieldSourcesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return reasons;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.StartsWith(ReadFailureMarkerPrefix, StringComparison.Ordinal)) continue;
                var code = property.Name[ReadFailureMarkerPrefix.Length..];
                if (ReadFailureCodes.Contains(code)) reasons.Add("read_" + code);
            }
        }
        catch (JsonException) { }
        return reasons;
    }

    private static bool HasFilenameFallback(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.EnumerateObject().Any(property =>
                    property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() == "filename_fallback");
        }
        catch (JsonException) { return false; }
    }

    private static string? Text(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
