using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

internal static class TrackQueryIndexes
{
    // These are migration-owned readiness markers. Partial/standalone databases
    // keep the existing query until their writer has installed the read model.
    public static bool Ready(ReadOnlyLibraryDatabase database, string kind) =>
        Convert.ToString(CachedQuery.Scalar(database,
            "SELECT value FROM library_maintenance_flags WHERE key = ?",
            command => command.Add("local_track_query." + kind))) == "1";

    public static string Excludes(TrackFilterColumns columns, IReadOnlyList<CoreSearchExclude> excludes)
    {
        if (excludes.All(exclude => !exclude.Substring))
        {
            var fields = new[] { "title", "artist", "album", "album_artist", "genre", "path" };
            var predicates = excludes.Select(exclude => "NOT (" + string.Join(" OR ",
                exclude.Variants.SelectMany(_ => fields.Select(field => $"{field} LIKE ? ESCAPE '\\'"))) + ")");
            return $"(SELECT {string.Join(" AND ", predicates)} FROM local_track_exclude_fields WHERE track_id = {columns.Id})";
        }
        return string.Join(" AND ", excludes.Select(exclude =>
        {
            // CJK substring matching keeps its existing literal/case rules.
            if (exclude.Substring) return SqlFragments.ExcludeSql(columns, [exclude]);
            var fields = new[] { "title", "artist", "album", "album_artist", "genre", "path" };
            var parts = exclude.Variants.SelectMany(_ => fields.Select(field => $"{field} LIKE ? ESCAPE '\\'"));
            // One indexed row lookup also keeps selective FTS/folder searches
            // selective; it never scans the whole derived table for a few hits.
            return $"(SELECT NOT ({string.Join(" OR ", parts)}) FROM local_track_exclude_fields WHERE track_id = {columns.Id})";
        }));
    }

    public static string EntityTrackSource(ReadOnlyLibraryDatabase database) =>
        CachedQuery.Scalar(database, "SELECT 1 FROM sqlite_master WHERE type='index' AND name='idx_track_entity_facts'") is null
            ? "tracks" : "tracks INDEXED BY idx_track_entity_facts";

    // Inspect at most 2049 folder-index entries, with no candidate list retained.
    // Large folders keep the ordered scan; tiny/empty folders drive the join.
    public static bool PreferFolderSource(ReadOnlyLibraryDatabase database, NormalizedPage page)
    {
        if (page.FolderIds.Count == 0) return false;
        var sql = $"SELECT COUNT(*) FROM (SELECT 1 FROM tracks WHERE folder_id IN ({SqlFragments.Placeholders(page.FolderIds.Count)}) LIMIT 2049)";
        return Convert.ToInt64(CachedQuery.Scalar(database, sql, command =>
        {
            foreach (var id in page.FolderIds) command.Add(id);
        })) <= 2048;
    }

    public static string FolderSource => "FROM tracks INDEXED BY idx_tracks_folder_id CROSS JOIN local_track_sort_keys AS sort_keys ON sort_keys.track_id = tracks.id";

    public static string? OrderedSource(IReadOnlyList<string> sorts, bool playbackReady, bool refined = false)
    {
        var sort = sorts.Count > 0 ? sorts[0] : "default";
        if (refined)
        {
            var refinedIndex = sort switch
            {
                "playCountAsc" when playbackReady => "play_count_asc",
                "codecAsc" => "codec_asc", "codecDesc" => "codec_desc",
                "audioSpecAsc" => "audio_asc", "audioSpecDesc" => "audio_desc",
                "trackNumber" => "track_number",
                _ => null,
            };
            if (refinedIndex is not null)
                return $"FROM local_track_sort_keys AS sort_keys INDEXED BY idx_local_track_sort_{refinedIndex} CROSS JOIN tracks ON tracks.id = sort_keys.track_id";
        }
        var textIndex = sort switch
        {
            "default" or "title" or "titleAsc" or "titleDesc" => "idx_local_track_sort_keys_title",
            "artist" or "artistDesc" or "artistAlbum" => "idx_local_track_sort_artist",
            "album" or "albumDesc" => "idx_local_track_sort_album",
            "lastPlayed" when playbackReady => "idx_local_track_sort_last_played",
            "playCountAsc" or "playCountDesc" or "frequent" when playbackReady => "idx_local_track_sort_play_count",
            _ => null,
        };
        if (textIndex is not null)
            return $"FROM local_track_sort_keys AS sort_keys INDEXED BY {textIndex} CROSS JOIN tracks ON tracks.id = sort_keys.track_id";
        var numericIndex = sort switch
        {
            "durationAsc" or "durationDesc" => "duration",
            "yearAsc" => "year_asc", "yearDesc" => "year_desc",
            "audioSpecAsc" => "audio_asc", "audioSpecDesc" => "audio_desc",
            "createdAsc" or "createdDesc" => "created", "recent" => "updated",
            "fileModifiedAsc" or "fileModifiedDesc" => "modified",
            "bpmAsc" => "bpm_asc", "bpmDesc" => "bpm_desc",
            "bitrateAsc" or "bitrateDesc" => "bitrate",
            "qualityAsc" => "quality_asc", "qualityDesc" => "quality_desc",
            _ => null,
        };
        return numericIndex is null ? null
            : $"FROM tracks INDEXED BY idx_track_order_{numericIndex} CROSS JOIN local_track_sort_keys AS sort_keys ON sort_keys.track_id = tracks.id";
    }
}
