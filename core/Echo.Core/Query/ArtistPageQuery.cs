using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class ArtistPageQuery
{
    public static LibraryPageDto<LibraryArtistDto> Execute(
        ReadOnlyLibraryDatabase database,
        CoreLibraryPageQuery? query,
        CoreNormalizedEntitySearch? search = null)
    {
        SearchContract.EnsureExecutableEntity(query, search);
        if (!QueryCapabilities.SupportsArtists(query, search))
        {
            throw new UnsupportedQueryException("getArtists combination is not migrated.");
        }

        var page = PageQueryNormalizer.Normalize(query);
        var sort = QueryCapabilities.PrimarySort(query);
        var avatarPriority = query?.PrioritizeArtistAvatars == true;
        var textSort = QueryCapabilities.IsArtistTextSort(sort) || avatarPriority;
        if (textSort && !PersistedSortKeys.AreArtistKeysFresh(database))
        {
            throw new UnsupportedQueryException("local_artist_sort_keys are stale; writer must sync before Core can order artist pages.");
        }

        var sortJoin = textSort
            ? "INNER JOIN local_artist_sort_keys AS sort_keys ON sort_keys.artist_id = artists.id"
            : string.Empty;
        var lastPlayed = "(SELECT MAX(tracks.last_played_at) FROM artist_tracks INNER JOIN tracks ON tracks.id = artist_tracks.track_id WHERE artist_tracks.artist_id = artists.id AND tracks.missing = 0 AND tracks.library_hidden = 0)";
        var playCount = "COALESCE((SELECT SUM(COALESCE(tracks.play_count, 0)) FROM artist_tracks INNER JOIN tracks ON tracks.id = artist_tracks.track_id WHERE artist_tracks.artist_id = artists.id AND tracks.missing = 0 AND tracks.library_hidden = 0), 0)";
        var created = "(SELECT MAX(tracks.created_at) FROM artist_tracks INNER JOIN tracks ON tracks.id = artist_tracks.track_id WHERE artist_tracks.artist_id = artists.id AND tracks.missing = 0 AND tracks.library_hidden = 0)";
        var orderSql = ArtistOrderSql(sort, lastPlayed, playCount, created, avatarPriority);
        var like = SqlFragments.LikeEntityFilter(
            [
                "artists.name",
                "COALESCE(artists.sort_name, '')",
            ],
            search);
        var presentArtist = """
            EXISTS (
              SELECT 1
              FROM artist_tracks
              INNER JOIN tracks ON tracks.id = artist_tracks.track_id
              WHERE artist_tracks.artist_id = artists.id
                AND tracks.missing = 0 AND tracks.library_hidden = 0
            )
            """;
        var whereSql = string.IsNullOrEmpty(like.Sql) ? $"WHERE {presentArtist}" : $"WHERE {presentArtist} AND {like.Sql}";
        var countSql = $"""
            SELECT COUNT(*)
            FROM artists
            {sortJoin}
            {whereSql}
            """;
        var idSql = $"""
            SELECT artists.id
            FROM artists
            {(avatarPriority ? ArtistAvatarPrioritySql.Join : string.Empty)}
            {sortJoin}
            {whereSql}
            {orderSql}
            LIMIT ? OFFSET ?
            """;

        void BindSearch(Microsoft.Data.Sqlite.SqliteCommand command)
        {
            foreach (var parameter in like.Parameters)
            {
                command.Add(parameter);
            }
        }

        using var transaction = database.BeginDeferredRead();
        QueryPlanDiagnostics.Capture(database, "artists.count", countSql, BindSearch);
        QueryPlanDiagnostics.Capture(database, "artists.ids", idSql, command =>
        {
            BindSearch(command);
            command.Add(page.PageSize);
            command.Add(page.Offset);
        });

        var ids = new List<string>();
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
            }
        }

        var items = HydrateArtists(database, ids);
        var total = PageTotal.Resolve(
            page,
            items.Count,
            () => Convert.ToInt64(CachedQuery.Scalar(database, countSql, BindSearch) ?? 0));
        return new LibraryPageDto<LibraryArtistDto>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total,
            HasMore = page.Offset + items.Count < total,
        };
    }

    private static List<LibraryArtistDto> HydrateArtists(ReadOnlyLibraryDatabase database, List<string> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var sql = $"""
            SELECT
              artists.id,
              artists.artist_key,
              artists.name,
              artists.sort_name,
              artists.role,
              artists.track_count,
              artists.album_count,
              artists.cover_id,
              covers.source_type AS cover_source,
              artist_image_cache.status AS avatar_status,
              artist_image_cache.provider AS avatar_provider,
              artist_image_cache.source_hash AS avatar_source_hash,
              artist_image_cache.thumb_path AS avatar_thumb_path,
              artist_image_cache.medium_path AS avatar_medium_path,
              artist_image_cache.large_path AS avatar_large_path
            FROM artists
            LEFT JOIN covers ON covers.id = artists.cover_id
            LEFT JOIN artist_image_cache ON artist_image_cache.artist_key = artists.artist_key
            WHERE artists.id {SqlFragments.JsonEachIds}
            """;
        var byId = new Dictionary<string, LibraryArtistDto>(ids.Count, StringComparer.Ordinal);
        using var reader = CachedQuery.Reader(database, sql, command =>
        {
            command.Add(System.Text.Json.JsonSerializer.Serialize(ids));
        });
        var mapper = ArtistRowMapper.Bind(reader);
        while (reader.Read())
        {
            var artist = mapper.Map();
            byId[artist.Id] = artist;
        }

        var items = new List<LibraryArtistDto>(ids.Count);
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var artist))
            {
                items.Add(artist);
            }
        }

        return items;
    }

    private static string ArtistOrderSql(string sort, string lastPlayed, string playCount, string created, bool avatarPriority)
    {
        var nameOrder = "sort_keys.name_key COLLATE NOCASE ASC, sort_keys.name_text COLLATE NOCASE ASC, artists.id ASC";
        var tieBreak = avatarPriority ? nameOrder : "artists.id ASC";
        var order = sort switch
        {
            "frequent" or "trackCountDesc" => $"artists.track_count DESC, artists.album_count DESC, {tieBreak}",
            "trackCountAsc" => $"artists.track_count ASC, artists.album_count ASC, {tieBreak}",
            "albumCountDesc" => $"artists.album_count DESC, artists.track_count DESC, {tieBreak}",
            "lastPlayed" => $"{lastPlayed} IS NULL, {lastPlayed} DESC, {tieBreak}",
            "playCountDesc" => $"{playCount} DESC, {tieBreak}",
            "playCountAsc" => $"{playCount} ASC, {tieBreak}",
            "recent" or "createdDesc" => $"{created} IS NULL, {created} DESC, {tieBreak}",
            "random" => "RANDOM()",
            "titleDesc" => "sort_keys.name_key COLLATE NOCASE DESC, sort_keys.name_text COLLATE NOCASE DESC, artists.id ASC",
            _ => nameOrder,
        };
        return "ORDER BY " + (avatarPriority ? ArtistAvatarPrioritySql.Order + ", " : string.Empty) + order;
    }
}
