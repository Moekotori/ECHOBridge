using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

public static class FolderTracksQuery
{
    public static LibraryPageDto<LibraryTrackListRowDto> Execute(
        ReadOnlyLibraryDatabase database,
        CoreLibraryPageQuery? query,
        CoreNormalizedEntitySearch? search,
        bool allowUnindexedTextSort = false)
    {
        if (!QueryCapabilities.SupportsFolderTracks(query))
        {
            throw new UnsupportedQueryException("getFolderTracks combination is not migrated.");
        }

        SearchContract.EnsureExecutableEntity(query, search);
        var useTextKeys = PersistedSortKeys.UseTextKeys(database, PersistedSortKeys.AreTrackKeysFresh(database),
            allowUnindexedTextSort, "local_track_sort_keys are stale; writer must sync before Core can order folder pages.");
        var sortJoin = useTextKeys ? "INNER JOIN local_track_sort_keys AS sort_keys ON sort_keys.track_id = tracks.id" : "";
        var textColumns = useTextKeys
            ? "sort_keys.title_key, sort_keys.title_text, sort_keys.artist_key, sort_keys.artist_text, sort_keys.album_key, sort_keys.album_text"
            : "tracks.title AS title_key, tracks.title AS title_text, tracks.artist AS artist_key, tracks.artist AS artist_text, tracks.album AS album_key, tracks.album AS album_text";

        var page = PageQueryNormalizer.Normalize(query);
        var list = QueryCapabilities.IsListProjection(query);
        var recursive = query?.Recursive != false;
        var scope = FolderPathScopeBuilder.Resolve(database, query?.FolderId, query?.Path, recursive);
        var like = SqlFragments.LikeEntityFilter(
            [
                "tracks.title",
                "tracks.artist",
                "tracks.album",
                "tracks.album_artist",
                "COALESCE(tracks.genre, '')",
                "tracks.path",
            ],
            search);
        var insight = FolderInsightSql.For(query?.FolderInsight);
        var clauses = new List<string> { scope.Sql };
        var parameters = new List<object?>(scope.Parameters);
        if (like.Sql.Length > 0)
        {
            clauses.Add(like.Sql);
            parameters.AddRange(like.Parameters.Cast<object?>());
        }

        if (insight is not null)
        {
            clauses.Add(insight);
        }

        if (page.ExcludeTrackIds.Count > 0)
        {
            clauses.Add($"tracks.id NOT IN ({SqlFragments.Placeholders(page.ExcludeTrackIds.Count)})");
            parameters.AddRange(page.ExcludeTrackIds);
        }

        var whereSql = $"WHERE {string.Join(" AND ", clauses)}";
        var playback = TrackOrderSql.NeedsPlaybackStats(page.Sorts);
        var playbackCte = playback
            ? """
              local_playback_stats AS (
                SELECT track_id, SUM(completed_count) AS play_count, MAX(last_started_at) AS last_played_at
                FROM playback_history_stats
                WHERE media_type = 'local' AND track_id IS NOT NULL
                GROUP BY track_id
              ),
              """
            : string.Empty;
        var playbackColumns = playback
            ? "MAX(COALESCE(tracks.play_count, 0), COALESCE(local_playback_stats.play_count, 0)) AS play_count, local_playback_stats.last_played_at AS last_played_at"
            : "tracks.play_count, tracks.last_played_at";
        var playbackJoin = playback ? "LEFT JOIN local_playback_stats ON local_playback_stats.track_id = tracks.id" : string.Empty;
        var orderSql = TrackOrderSql.ForSorts(page.Sorts, searchActive: false);
        var countSql = $"""
            SELECT COUNT(*)
            FROM tracks
            {sortJoin}
            {whereSql}
            """;
        var idSql = $"""
            WITH {playbackCte} folder_ids AS (
              SELECT
                tracks.id,
                tracks.updated_at,
                tracks.year,
                tracks.duration,
                tracks.mtime_ms,
                tracks.size_bytes,
                tracks.bitrate,
                tracks.codec,
                tracks.sample_rate,
                tracks.bit_depth,
                {playbackColumns},
                0 AS search_rank,
                {textColumns},
                tracks.disc_no,
                tracks.track_no
              FROM tracks
              {sortJoin}
              {playbackJoin}
              {whereSql}
            )
            SELECT id
            FROM folder_ids
            {orderSql}
            LIMIT ? OFFSET ?
            """;

        using var transaction = database.BeginDeferredRead();
        var total = Convert.ToInt64(CachedQuery.Scalar(database, countSql, command => Bind(command, parameters)) ?? 0);
        var ids = new List<string>();
        using (var reader = CachedQuery.Reader(database, idSql, command =>
        {
            Bind(command, parameters);
            command.Add(page.PageSize);
            command.Add(page.Offset);
        }))
        {
            while (reader.Read())
            {
                ids.Add(Convert.ToString(reader[0]) ?? string.Empty);
            }
        }

        var items = TrackPageQuery.HydrateTracks(database, ids, list);
        if (playback)
        {
            OverlayPlaybackStats(database, items);
        }

        return new LibraryPageDto<LibraryTrackListRowDto>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total,
            HasMore = page.Offset + items.Count < total,
        };
    }

    private static void Bind(SqliteCommand command, IReadOnlyList<object?> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Add(parameter);
        }
    }

    private static void OverlayPlaybackStats(ReadOnlyLibraryDatabase database, List<LibraryTrackListRowDto> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var sql = $"""
            SELECT tracks.id, MAX(COALESCE(tracks.play_count, 0), COALESCE(SUM(history.completed_count), 0)) AS play_count,
                   MAX(history.last_started_at) AS last_played_at
            FROM tracks LEFT JOIN playback_history_stats history ON history.track_id=tracks.id AND history.media_type='local'
            WHERE tracks.id {SqlFragments.JsonEachIds}
            GROUP BY tracks.id
            """;
        var byId = items.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        using var reader = CachedQuery.Reader(
            database,
            sql,
            command => command.Add(System.Text.Json.JsonSerializer.Serialize(items.ConvertAll(static item => item.Id))));
        while (reader.Read())
        {
            var id = Convert.ToString(reader[0]);
            if (id is null || !byId.TryGetValue(id, out var track))
            {
                continue;
            }

            track.PlayCount = Math.Max(track.PlayCount, Convert.ToInt32(reader[1] ?? 0));
            track.LastPlayedAt = Mapping.LibraryText.TextOrNull(reader.IsDBNull(2) ? null : reader.GetValue(2));
        }
    }
}
