using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class AlbumPageQuery
{
    public static LibraryPageDto<LibraryAlbumDto> Execute(
        ReadOnlyLibraryDatabase database,
        CoreLibraryPageQuery? query,
        CoreNormalizedEntitySearch? search = null,
        bool allowUnindexedTextSort = false)
    {
        SearchContract.EnsureExecutableEntity(query, search);
        if (!QueryCapabilities.SupportsAlbums(query, search))
        {
            throw new UnsupportedQueryException("getAlbums combination is not migrated.");
        }

        var page = PageQueryNormalizer.Normalize(query);
        var sort = QueryCapabilities.PrimarySort(query);
        var textSort = QueryCapabilities.IsAlbumTextSort(sort);
        var useTextKeys = !textSort || PersistedSortKeys.UseTextKeys(database, PersistedSortKeys.AreAlbumKeysFresh(database),
            allowUnindexedTextSort, "local_album_sort_keys are stale; writer must sync before Core can order album pages.");

        var playback = sort is "lastPlayed" or "playCountAsc" or "playCountDesc";
        var trackSource = TrackQueryIndexes.EntityTrackSource(database);
        var osuSql = query?.ExcludeOsuAlbums == true
            ? """
              AND EXISTS (
                SELECT 1
                FROM album_tracks
                INNER JOIN tracks ON tracks.id = album_tracks.track_id
                WHERE album_tracks.album_id = albums.id
                  AND tracks.missing = 0 AND tracks.library_hidden = 0
                  AND NOT (lower(trim(COALESCE(json_extract(tracks.field_sources_json, '$.osu'), ''))) = 'osu'
                    OR lower(trim(COALESCE(tracks.album, ''))) LIKE 'osu! beatmapset %')
              )
              """
            : string.Empty;
        var presentSql = VisibleAlbumSql.Predicate.Replace("INNER JOIN tracks", $"INNER JOIN {trackSource}", StringComparison.Ordinal);
        var sortJoin = textSort && useTextKeys
            ? "INNER JOIN local_album_sort_keys AS sort_keys ON sort_keys.album_id = albums.id"
            : string.Empty;
        var indexedPlayback = playback && trackSource != "tracks" && TrackQueryIndexes.Ready(database, "playback")
            && PersistedSortKeys.AreTrackKeysFresh(database);
        var playbackCtes = indexedPlayback ? $"""
              WITH local_album_playback_stats AS (
                SELECT album_tracks.album_id, SUM(sort_keys.playback_sort_count) AS play_count,
                       MAX(sort_keys.playback_sort_last) AS last_played_at
                FROM album_tracks
                INNER JOIN {trackSource} ON tracks.id = album_tracks.track_id
                INNER JOIN local_track_sort_keys AS sort_keys ON sort_keys.track_id = tracks.id
                WHERE tracks.missing = 0 AND tracks.library_hidden = 0
                GROUP BY album_tracks.album_id
              )
              """ : playback
            ? $"""
              WITH local_track_playback_stats AS (
                SELECT track_id, SUM(completed_count) AS play_count, MAX(last_started_at) AS last_played_at
                FROM playback_history_stats
                WHERE media_type = 'local' AND track_id IS NOT NULL
                GROUP BY track_id
              ),
              local_album_playback_stats AS (
                SELECT album_tracks.album_id,
                       COALESCE(SUM(MAX(COALESCE(tracks.play_count, 0), COALESCE(local_track_playback_stats.play_count, 0))), 0) AS play_count,
                       MAX(local_track_playback_stats.last_played_at) AS last_played_at
                FROM album_tracks
                INNER JOIN {trackSource} ON tracks.id = album_tracks.track_id
                LEFT JOIN local_track_playback_stats ON local_track_playback_stats.track_id = album_tracks.track_id
                WHERE tracks.missing = 0 AND tracks.library_hidden = 0
                GROUP BY album_tracks.album_id
              )
              """
            : string.Empty;
        var playbackJoin = playback
            ? "LEFT JOIN local_album_playback_stats ON local_album_playback_stats.album_id = albums.id"
            : string.Empty;
        var addedAt = $"""
            COALESCE((
              SELECT MAX(tracks.created_at)
              FROM album_tracks
              INNER JOIN {trackSource} ON tracks.id = album_tracks.track_id
              WHERE album_tracks.album_id = albums.id AND tracks.missing = 0 AND tracks.library_hidden = 0
            ), albums.created_at)
            """;
        var mtime = $"""
            COALESCE((
              SELECT MAX(tracks.mtime_ms)
              FROM album_tracks
              INNER JOIN {trackSource} ON tracks.id = album_tracks.track_id
              WHERE album_tracks.album_id = albums.id AND tracks.missing = 0 AND tracks.library_hidden = 0
            ), 0)
            """;
        var orderSql = AlbumOrderSql(sort, addedAt, mtime, useTextKeys);
        var like = AlbumSearchSql.Filter(search);
        var likeSql = string.IsNullOrEmpty(like.Sql) ? string.Empty : $"AND {like.Sql}";
        var focus = AlbumFocusSql.WhereSql(query?.AlbumFocus);
        var focusSql = string.IsNullOrEmpty(focus.Sql) ? string.Empty : $"AND {focus.Sql}";
        var personalTagSql = !string.IsNullOrEmpty(query?.PersonalTagId) ? $"AND {PersonalTagAlbumSql.Sql}" : string.Empty;
        var versionsSql = query?.CollapseAlbumVersions == true ? $"AND {AlbumVersionVisibility.Sql}" : string.Empty;
        var countSql = $"""
            SELECT COUNT(*)
            FROM albums
            {sortJoin}
            WHERE {presentSql}
            {osuSql}
            {likeSql}
            {focusSql}
            {versionsSql}
            {personalTagSql}
            """;
        var idPrefix = string.IsNullOrEmpty(playbackCtes) ? string.Empty : $"{playbackCtes} ";
        // Search predicates can aggregate every album's track text. Count in the
        // same pass rather than evaluating that exact predicate a second time.
        var countWithPage = like.Sql.Length > 0 && page.TotalHint is null;
        var idSql = $"""
            {idPrefix}SELECT albums.id{(countWithPage ? ", COUNT(*) OVER ()" : string.Empty)}
            FROM albums
            {sortJoin}
            {playbackJoin}
            WHERE {presentSql}
            {osuSql}
            {likeSql}
            {focusSql}
            {versionsSql}
            {personalTagSql}
            {orderSql}
            LIMIT ? OFFSET ?
            """;

        void BindSearch(Microsoft.Data.Sqlite.SqliteCommand command)
        {
            foreach (var parameter in like.Parameters)
            {
                command.Add(parameter);
            }

            foreach (var parameter in focus.Parameters)
            {
                command.Add(parameter);
            }
            if (!string.IsNullOrEmpty(query?.PersonalTagId)) command.Add(query.PersonalTagId);
        }

        using var transaction = database.BeginDeferredRead();
        QueryPlanDiagnostics.Capture(database, "albums.count", countSql, BindSearch);
        QueryPlanDiagnostics.Capture(database, "albums.ids", idSql, command =>
        {
            BindSearch(command);
            command.Add(page.PageSize);
            command.Add(page.Offset);
        });

        var ids = new List<string>();
        long? countedTotal = null;
        using (var reader = CachedQuery.Reader(database, idSql, command =>
        {
            BindSearch(command);
            command.Add(page.PageSize);
            command.Add(page.Offset);
        }))
        {
            while (reader.Read())
            {
                ids.Add(Convert.ToString(reader[0]) ?? string.Empty);
                if (countWithPage) countedTotal = reader.GetInt64(1);
            }
        }

        var items = HydrateAlbums(database, ids);
        var total = countedTotal ?? PageTotal.Resolve(
            page,
            items.Count,
            () => Convert.ToInt64(CachedQuery.Scalar(database, countSql, BindSearch) ?? 0));
        return new LibraryPageDto<LibraryAlbumDto>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total,
            HasMore = page.Offset + items.Count < total,
        };
    }

    internal static List<LibraryAlbumDto> HydrateAlbums(ReadOnlyLibraryDatabase database, List<string> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var sql = $"""
            SELECT
              albums.id,
              albums.album_key,
              albums.title,
              albums.album_artist,
              albums.year,
              albums.track_count,
              albums.duration,
              COALESCE(covers.id, albums.cover_id) AS cover_id,
              covers.source_type AS cover_source
            FROM albums
            {AlbumDisplayCoverSql.Joins}
            WHERE albums.id {SqlFragments.JsonEachIds}
            """;
        var byId = new Dictionary<string, LibraryAlbumDto>(ids.Count, StringComparer.Ordinal);
        using var reader = CachedQuery.Reader(database, sql, command =>
        {
            command.Add(System.Text.Json.JsonSerializer.Serialize(ids));
        });
        var mapper = AlbumRowMapper.Bind(reader);
        while (reader.Read())
        {
            var album = mapper.Map();
            byId[album.Id] = album;
        }

        var items = new List<LibraryAlbumDto>(ids.Count);
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var album))
            {
                items.Add(album);
            }
        }

        return items;
    }

    private static string AlbumOrderSql(string sort, string addedAt, string mtime, bool useTextKeys)
    {
        var titleAsc = useTextKeys ? "sort_keys.title_key COLLATE NOCASE ASC, sort_keys.title_text COLLATE NOCASE ASC" : "albums.title COLLATE NOCASE ASC";
        var titleDesc = useTextKeys ? "sort_keys.title_key COLLATE NOCASE DESC, sort_keys.title_text COLLATE NOCASE DESC" : "albums.title COLLATE NOCASE DESC";
        var artistAsc = useTextKeys ? "sort_keys.artist_key COLLATE NOCASE ASC, sort_keys.artist_text COLLATE NOCASE ASC" : "albums.album_artist COLLATE NOCASE ASC";
        var artistDesc = useTextKeys ? "sort_keys.artist_key COLLATE NOCASE DESC, sort_keys.artist_text COLLATE NOCASE DESC" : "albums.album_artist COLLATE NOCASE DESC";
        return sort switch
        {
            "recent" => $"ORDER BY {addedAt} DESC, albums.updated_at DESC, albums.id ASC",
            "lastPlayed" => "ORDER BY local_album_playback_stats.last_played_at IS NULL, local_album_playback_stats.last_played_at DESC, albums.id ASC",
            "playCountAsc" => "ORDER BY COALESCE(local_album_playback_stats.play_count, 0) ASC, local_album_playback_stats.last_played_at DESC, albums.id ASC",
            "playCountDesc" => "ORDER BY COALESCE(local_album_playback_stats.play_count, 0) DESC, local_album_playback_stats.last_played_at DESC, albums.id ASC",
            "trackCountAsc" => "ORDER BY albums.track_count ASC, albums.id ASC",
            "trackCountDesc" => "ORDER BY albums.track_count DESC, albums.id ASC",
            "yearAsc" => "ORDER BY albums.year IS NULL, albums.year ASC, albums.id ASC",
            "yearDesc" => "ORDER BY albums.year IS NULL, albums.year DESC, albums.id ASC",
            "createdDesc" => "ORDER BY albums.created_at DESC, albums.id ASC",
            "createdAsc" => "ORDER BY albums.created_at ASC, albums.id ASC",
            "durationAsc" => "ORDER BY albums.duration ASC, albums.id ASC",
            "durationDesc" => "ORDER BY albums.duration DESC, albums.id ASC",
            "fileModifiedAsc" => $"ORDER BY {mtime} ASC, albums.id ASC",
            "fileModifiedDesc" => $"ORDER BY {mtime} DESC, albums.id ASC",
            "random" => "ORDER BY RANDOM()",
            "titleDesc" => $"ORDER BY {titleDesc}, {artistAsc}, albums.id ASC",
            "artist" or "artistAlbum" => $"ORDER BY {artistAsc}, {titleAsc}, albums.id ASC",
            "artistDesc" => $"ORDER BY {artistDesc}, {titleAsc}, albums.id ASC",
            _ => $"ORDER BY {titleAsc}, {artistAsc}, albums.id ASC",
        };
    }
}
