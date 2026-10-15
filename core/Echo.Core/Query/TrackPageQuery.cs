using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

public static class TrackPageQuery
{
    public static LibraryPageDto<LibraryTrackListRowDto> Execute(
        ReadOnlyLibraryDatabase database,
        CoreLibraryPageQuery? query,
        CoreNormalizedTrackSearch? search)
    {
        if (!QueryCapabilities.SupportsTracks(query, search))
        {
            throw new UnsupportedQueryException("getTracks combination is not migrated.");
        }

        SearchContract.EnsureExecutable(query, search);

        var page = PageQueryNormalizer.Normalize(query);
        var hasFts = !string.IsNullOrWhiteSpace(search?.FtsMatch);
        var list = QueryCapabilities.IsListProjection(query);
        if (FolderInsightSql.For(query?.FolderInsight) is null
            && QueryCapabilities.IsLocalTitleSort(query, hasFts)
            && TryReadIndexedTitlePage(database, query, page, search, list) is { } indexed)
        {
            return indexed;
        }

        return ReadUnifiedLocalPage(database, query, page, search, hasFts, list);
    }

    private static LibraryPageDto<LibraryTrackListRowDto>? TryReadIndexedTitlePage(
        ReadOnlyLibraryDatabase database,
        CoreLibraryPageQuery? query,
        NormalizedPage page,
        CoreNormalizedTrackSearch? search,
        bool list)
    {
        if (!PersistedSortKeys.AreTrackKeysFresh(database) || page.ShowDuplicatesOnly)
        {
            return null;
        }

        var columns = TrackFilterColumns.Local;
        var extra = BuildTrackPredicates(query, page, search, columns, includeYearAndExclude: true);
        var duplicateJoin = page.HideDuplicates
            ? SqlFragments.DuplicateMembersJoin("sort_keys.track_id")
            : string.Empty;
        var duplicateFilter = page.HideDuplicates ? "COALESCE(duplicate_members.hidden, 0) = 0" : string.Empty;
        var hasTrackPredicates = extra.Sql.Length > 0;
        var items = new List<LibraryTrackListRowDto>();
        long total;
        using var transaction = database.BeginDeferredRead();

        if (!hasTrackPredicates)
        {
            var filterSql = SqlFragments.And(
                "WHERE sort_keys.track_id NOT IN (SELECT id FROM tracks WHERE library_hidden = 1)",
                duplicateFilter);
            var countSql = $"""
                SELECT COUNT(*) FROM local_track_sort_keys AS sort_keys
                {duplicateJoin}
                {filterSql}
                """;
            var pageSql = IndexedTrackTitleSql.Page(list, duplicateJoin, filterSql);
            QueryPlanDiagnostics.Capture(database, "tracks.indexed.count", countSql, command => BindDuplicateAndExtra(command, page, extra));
            QueryPlanDiagnostics.Capture(database, "tracks.indexed.page", pageSql, command =>
            {
                BindDuplicateAndExtra(command, page, extra);
                command.Add(page.PageSize);
                command.Add(page.Offset);
            });
            ReadTrackRows(
                database,
                pageSql,
                command =>
                {
                    BindDuplicateAndExtra(command, page, extra);
                    command.Add(page.PageSize);
                    command.Add(page.Offset);
                },
                items,
                list);
            total = ResolveIndexedTotal(
                database,
                page,
                items.Count,
                hasTrackPredicates: false,
                countSql,
                extra);
        }
        else
        {
            var trackWhere = SqlFragments.And("WHERE tracks.missing = 0 AND tracks.library_hidden = 0", string.Join(" AND ", new[] { duplicateFilter, extra.Sql }.Where(static part => part.Length > 0)));
            var pageSql = $"""
                SELECT {TrackSelectSql.ForList(list)}
                FROM local_track_sort_keys AS sort_keys
                INNER JOIN tracks ON tracks.id = sort_keys.track_id
                {duplicateJoin}
                {trackWhere}
                ORDER BY {TrackOrderSql.IndexedTitleOrder}
                LIMIT ? OFFSET ?
                """;
            var countSql = $"""
                SELECT COUNT(*)
                FROM local_track_sort_keys AS sort_keys
                INNER JOIN tracks ON tracks.id = sort_keys.track_id
                {duplicateJoin}
                {trackWhere}
                """;
            QueryPlanDiagnostics.Capture(database, "tracks.indexedFiltered.count", countSql, command => BindDuplicateAndExtra(command, page, extra));
            QueryPlanDiagnostics.Capture(database, "tracks.indexedFiltered.page", pageSql, command =>
            {
                BindDuplicateAndExtra(command, page, extra);
                command.Add(page.PageSize);
                command.Add(page.Offset);
            });
            ReadTrackRows(
                database,
                pageSql,
                command =>
                {
                    BindDuplicateAndExtra(command, page, extra);
                    command.Add(page.PageSize);
                    command.Add(page.Offset);
                },
                items,
                list);
            total = ResolveIndexedTotal(
                database,
                page,
                items.Count,
                hasTrackPredicates: true,
                countSql,
                extra);
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

    private static LibraryPageDto<LibraryTrackListRowDto> ReadUnifiedLocalPage(
        ReadOnlyLibraryDatabase database,
        CoreLibraryPageQuery? query,
        NormalizedPage page,
        CoreNormalizedTrackSearch? search,
        bool hasFts,
        bool list)
    {
        if (!PersistedSortKeys.AreTrackKeysFresh(database))
        {
            throw new UnsupportedQueryException("local_track_sort_keys are stale; writer must sync before Core can order local pages.");
        }

        var columns = TrackFilterColumns.Local;
        var extra = BuildTrackPredicates(query, page, search, columns, includeYearAndExclude: true);
        var useDuplicateJoin = page.HideDuplicates || page.ShowDuplicatesOnly;
        var duplicateJoin = useDuplicateJoin
            ? SqlFragments.DuplicateMembersJoin("tracks.id")
            : string.Empty;
        var duplicateFilter = page.ShowDuplicatesOnly
            ? "duplicate_members.track_id IS NOT NULL"
            : page.HideDuplicates ? "COALESCE(duplicate_members.hidden, 0) = 0" : string.Empty;
        var ftsJoin = hasFts ? "INNER JOIN tracks_fts ON tracks_fts.rowid = tracks.rowid" : string.Empty;
        var searchRank = hasFts ? "bm25(tracks_fts)" : "0";
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
            ? "COALESCE(local_playback_stats.play_count, 0) AS play_count, local_playback_stats.last_played_at AS last_played_at"
            : "tracks.play_count, tracks.last_played_at";
        var playbackJoin = playback ? "LEFT JOIN local_playback_stats ON local_playback_stats.track_id = tracks.id" : string.Empty;
        var whereSql = SqlFragments.And(
            hasFts
                ? $"WHERE tracks.missing = 0 AND tracks.library_hidden = 0{(duplicateFilter.Length > 0 ? $" AND {duplicateFilter}" : string.Empty)} AND tracks_fts MATCH ?"
                : $"WHERE tracks.missing = 0 AND tracks.library_hidden = 0{(duplicateFilter.Length > 0 ? $" AND {duplicateFilter}" : string.Empty)}",
            extra.Sql);
        var orderSql = TrackOrderSql.ForSorts(page.Sorts, hasFts);
        // Prefix search starts at the FTS hit list. The page projection keeps only
        // the columns that ORDER BY reads, then hydrates that one page.
        var searchRelations = $"""
            FROM tracks_fts
            INNER JOIN tracks ON tracks.rowid = tracks_fts.rowid
            INNER JOIN local_track_sort_keys AS sort_keys ON sort_keys.track_id = tracks.id
            {duplicateJoin}
            """;
        var searchHitSelect = TrackOrderSql.SearchHitSelect(
            page.Sorts,
            searchActive: true,
            searchRankSql: "bm25(tracks_fts)",
            playCountSql: playback ? "COALESCE(local_playback_stats.play_count, 0)" : "tracks.play_count",
            lastPlayedSql: playback ? "local_playback_stats.last_played_at" : "tracks.last_played_at");
        var countSql = hasFts
            ? $"""
                SELECT COUNT(*)
                {searchRelations}
                {whereSql}
                """
            : $"""
                SELECT COUNT(*)
                FROM tracks
                INNER JOIN local_track_sort_keys AS sort_keys ON sort_keys.track_id = tracks.id
                {ftsJoin}
                {duplicateJoin}
                {whereSql}
                """;
        var idSql = hasFts
            ? $"""
                WITH {playbackCte} library_ids AS (
                  SELECT
                    {searchHitSelect}
                  {searchRelations}
                  {playbackJoin}
                  {whereSql}
                )
                SELECT id
                FROM library_ids
                {orderSql}
                LIMIT ? OFFSET ?
                """
            : $"""
                WITH {playbackCte} library_ids AS (
                  SELECT
                    tracks.id,
                    tracks.path,
                    tracks.created_at,
                    tracks.updated_at,
                    tracks.year,
                    tracks.duration,
                    tracks.mtime_ms,
                    tracks.size_bytes,
                    tracks.bitrate,
                    tracks.bpm,
                    tracks.codec,
                    tracks.sample_rate,
                    tracks.bit_depth,
                    {playbackColumns},
                    {searchRank} AS search_rank,
                    sort_keys.title_key,
                    sort_keys.title_text,
                    sort_keys.artist_key,
                    sort_keys.artist_text,
                    sort_keys.album_key,
                    sort_keys.album_text,
                    tracks.disc_no,
                    tracks.track_no
                  FROM tracks
                  INNER JOIN local_track_sort_keys AS sort_keys ON sort_keys.track_id = tracks.id
                  {ftsJoin}
                  {duplicateJoin}
                  {playbackJoin}
                  {whereSql}
                )
                SELECT id
                FROM library_ids
                {orderSql}
                LIMIT ? OFFSET ?
                """;

        using var transaction = database.BeginDeferredRead();
        QueryPlanDiagnostics.Capture(database, "tracks.unified.count", countSql, command => BindUnified(command, page, extra, search, hasFts, useDuplicateJoin, includeLimit: false));
        QueryPlanDiagnostics.Capture(database, "tracks.unified.ids", idSql, command => BindUnified(command, page, extra, search, hasFts, useDuplicateJoin, includeLimit: true));
        var total = page.TotalHint ?? Count(
            database,
            countSql,
            command => BindUnified(command, page, extra, search, hasFts, useDuplicateJoin, includeLimit: false));
        var ids = new List<string>();
        ReadRows(database, idSql, command => BindUnified(command, page, extra, search, hasFts, useDuplicateJoin, includeLimit: true), reader =>
        {
            ids.Add(Convert.ToString(reader[0]) ?? string.Empty);
        });
        var items = HydrateTracks(database, ids, list);
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

    private static PredicateSql BuildTrackPredicates(
        CoreLibraryPageQuery? query,
        NormalizedPage page,
        CoreNormalizedTrackSearch? search,
        TrackFilterColumns columns,
        bool includeYearAndExclude)
    {
        var clauses = new List<string>();
        var parameters = new List<object?>();
        if (page.FolderIds.Count > 0)
        {
            clauses.Add($"tracks.folder_id IN ({SqlFragments.Placeholders(page.FolderIds.Count)})");
            parameters.AddRange(page.FolderIds);
        }

        var audio = SqlFragments.AudioFormatSql(query?.AudioFormatFilter, columns);
        if (audio.Length > 0)
        {
            clauses.Add(audio);
        }

        var file = SqlFragments.FileFormatSql(query?.FileFormatFilter, columns);
        if (file.Length > 0)
        {
            clauses.Add(file);
        }

        var vocal = VocalPresenceSql.Filter(query?.VocalFilter, columns.Id, columns.Title, columns.Album);
        if (vocal.Length > 0)
        {
            clauses.Add(vocal);
        }

        if (page.ShowOsuOnly)
        {
            clauses.Add(SqlFragments.OsuImportedSql(columns));
        }

        if (includeYearAndExclude && search?.Year is { } year)
        {
            clauses.Add($"{columns.Year} = ?");
            parameters.Add(year);
        }

        if (includeYearAndExclude && search?.Excludes is { Count: > 0 } excludes)
        {
            clauses.Add(SqlFragments.ExcludeSql(columns, excludes));
            parameters.AddRange(SqlFragments.ExcludeParams(excludes));
        }

        if (page.ExcludeTrackIds.Count > 0)
        {
            clauses.Add($"{columns.Id} NOT IN ({SqlFragments.Placeholders(page.ExcludeTrackIds.Count)})");
            parameters.AddRange(page.ExcludeTrackIds);
        }

        var insight = FolderInsightSql.For(query?.FolderInsight);
        if (insight is not null)
        {
            clauses.Add(insight);
        }

        return new PredicateSql(string.Join(" AND ", clauses), parameters);
    }

    private static void BindUnified(
        SqliteCommand command,
        NormalizedPage page,
        PredicateSql extra,
        CoreNormalizedTrackSearch? search,
        bool hasFts,
        bool useDuplicateJoin,
        bool includeLimit)
    {
        if (useDuplicateJoin)
        {
            command.Add(page.DuplicateMode);
        }

        if (hasFts)
        {
            command.Add(search!.FtsMatch);
        }

        BindExtra(command, extra);
        if (includeLimit)
        {
            command.Add(page.PageSize);
            command.Add(page.Offset);
        }
    }

    internal static string HydrationSql(bool list) => $"""
        SELECT {TrackSelectSql.ForList(list)}
        FROM json_each(?) AS requested
        INNER JOIN tracks ON tracks.id = requested.value
        """;

    public static List<LibraryTrackListRowDto> HydrateTracks(ReadOnlyLibraryDatabase database, List<string> ids, bool list)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // json_each has to be the outer input. `id IN (SELECT json_each)` does not
        // tell SQLite the list is tiny, so a play click scanned the whole tracks table.
        var sql = HydrationSql(list);
        var mapped = new List<LibraryTrackListRowDto>(ids.Count);
        ReadTrackRows(database, sql, command => command.Add(System.Text.Json.JsonSerializer.Serialize(ids)), mapped, list);
        var byId = new Dictionary<string, LibraryTrackListRowDto>(mapped.Count, StringComparer.Ordinal);
        foreach (var track in mapped)
        {
            byId[track.Id] = track;
        }
        var items = new List<LibraryTrackListRowDto>(ids.Count);
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var track))
            {
                items.Add(track);
            }
        }

        return items;
    }

    private static void OverlayPlaybackStats(ReadOnlyLibraryDatabase database, List<LibraryTrackListRowDto> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var sql = $"""
            SELECT track_id, SUM(completed_count) AS play_count, MAX(last_started_at) AS last_played_at
            FROM playback_history_stats
            WHERE media_type = 'local'
              AND track_id {SqlFragments.JsonEachIds}
            GROUP BY track_id
            """;
        var byId = items.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        ReadRows(database, sql, command => command.Add(System.Text.Json.JsonSerializer.Serialize(items.ConvertAll(static item => item.Id))), reader =>
        {
            var id = Convert.ToString(reader[0]);
            if (id is null || !byId.TryGetValue(id, out var track))
            {
                return;
            }

            track.PlayCount = Math.Max(0, Convert.ToInt32(reader[1] ?? 0));
            track.LastPlayedAt = LibraryText.TextOrNull(reader.IsDBNull(2) ? null : reader.GetValue(2));
        });
    }

    private static void BindDuplicateAndExtra(SqliteCommand command, NormalizedPage page, PredicateSql extra)
    {
        if (page.HideDuplicates)
        {
            command.Add(page.DuplicateMode);
        }

        BindExtra(command, extra);
    }

    private static void BindExtra(SqliteCommand command, PredicateSql extra)
    {
        foreach (var parameter in extra.Parameters)
        {
            command.Add(parameter);
        }
    }

    private static long ResolveIndexedTotal(
        ReadOnlyLibraryDatabase database,
        NormalizedPage page,
        int itemCount,
        bool hasTrackPredicates,
        string countSql,
        PredicateSql extra)
    {
        if (!hasTrackPredicates && page.TotalHint is null
            && PersistedSortKeys.TryTrackPresentCount(database) is { } cached)
        {
            var libraryHidden = Count(database,
                "SELECT COUNT(*) FROM tracks WHERE library_hidden = 1 AND missing = 0", _ => { });
            var visible = Math.Max(0, cached - libraryHidden);
            if (!page.HideDuplicates)
            {
                if (page.Offset > 0 || itemCount >= page.PageSize)
                {
                    return visible;
                }
            }
            else
            {
                // Counting every sort key through the duplicate join reads the
                // whole library. Hidden members are the small side.
                var hidden = Count(database, """
                    SELECT COUNT(*)
                    FROM duplicate_track_members AS members
                    INNER JOIN duplicate_track_groups AS groups ON groups.id = members.group_id
                    INNER JOIN local_track_sort_keys AS sort_keys ON sort_keys.track_id = members.track_id
                    WHERE groups.mode = ? AND members.hidden = 1
                      AND members.track_id NOT IN (SELECT id FROM tracks WHERE library_hidden = 1)
                    """, command => command.Add(page.DuplicateMode));
                return Math.Max(0, visible - hidden);
            }
        }

        return PageTotal.Resolve(
            page,
            itemCount,
            () => Count(database, countSql, command => BindDuplicateAndExtra(command, page, extra)));
    }

    private static long Count(ReadOnlyLibraryDatabase database, string sql, Action<SqliteCommand> bind)
    {
        return Convert.ToInt64(CachedQuery.Scalar(database, sql, bind) ?? 0);
    }

    private static void ReadTrackRows(
        ReadOnlyLibraryDatabase database,
        string sql,
        Action<SqliteCommand> bind,
        List<LibraryTrackListRowDto> items,
        bool list = false)
    {
        using var reader = CachedQuery.Reader(database, sql, bind);
        if (list)
        {
            var mapper = TrackListRowMapper.Bind(reader);
            while (reader.Read())
            {
                items.Add(mapper.Map());
            }

            DefaultCoverLookup.SuppressTrackCovers(database, items);
            return;
        }

        var full = TrackRowMapper.Bind(reader);
        while (reader.Read())
        {
            items.Add(full.Map());
        }

        DefaultCoverLookup.SuppressTrackCovers(database, items);
    }

    private static void ReadRows(
        ReadOnlyLibraryDatabase database,
        string sql,
        Action<SqliteCommand> bind,
        Action<SqliteDataReader> consume)
    {
        using var reader = CachedQuery.Reader(database, sql, bind);
        while (reader.Read())
        {
            consume(reader);
        }
    }

    private readonly record struct PredicateSql(string Sql, IReadOnlyList<object?> Parameters);
}

public sealed class UnsupportedQueryException : Exception
{
    public UnsupportedQueryException(string message)
        : base(message)
    {
    }
}
