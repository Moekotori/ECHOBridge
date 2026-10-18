using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class GenrePageQuery
{
    public static LibraryPageDto<LibraryGenreDto> Execute(
        ReadOnlyLibraryDatabase database,
        CoreLibraryPageQuery? query,
        bool allowUnindexedTextSort = false)
    {
        if (!QueryCapabilities.SupportsGenres(query))
        {
            throw new UnsupportedQueryException("getGenres combination is not migrated.");
        }

        if (!PersistedSortKeys.AreGenrePagesFresh(database))
        {
            if (allowUnindexedTextSort) return UnindexedGenrePageQuery.Execute(database, query);
            throw new UnsupportedQueryException("local_genre_pages are stale; writer must sync before Core can serve genre pages.");
        }

        var page = PageQueryNormalizer.Normalize(query);
        var search = query?.Search?.Trim() ?? string.Empty;
        var likeSql = string.IsNullOrEmpty(search) ? string.Empty : "AND display_name LIKE ? ESCAPE '\\'";
        var orderSql = GenreOrderSql(QueryCapabilities.PrimarySort(query));
        var countSql = $"""
            SELECT COUNT(*)
            FROM local_genre_pages
            WHERE 1 = 1
            {likeSql}
            """;
        var idSql = $"""
            SELECT genre_key
            FROM local_genre_pages
            WHERE 1 = 1
            {likeSql}
            {orderSql}
            LIMIT ? OFFSET ?
            """;

        void BindSearch(Microsoft.Data.Sqlite.SqliteCommand command)
        {
            if (!string.IsNullOrEmpty(search))
            {
                command.Add($"%{SqlFragments.EscapeLike(search)}%");
            }
        }

        using var transaction = database.BeginDeferredRead();
        QueryPlanDiagnostics.Capture(database, "genres.count", countSql, BindSearch);
        QueryPlanDiagnostics.Capture(database, "genres.ids", idSql, command =>
        {
            BindSearch(command);
            command.Add(page.PageSize);
            command.Add(page.Offset);
        });

        var keys = new List<string>();
        using (var reader = CachedQuery.Reader(database, idSql, command =>
        {
            BindSearch(command);
            command.Add(page.PageSize);
            command.Add(page.Offset);
        }))
        {
            while (reader.Read())
            {
                keys.Add(Convert.ToString(reader[0]) ?? string.Empty);
            }
        }

        var items = HydrateGenres(database, keys);
        var total = PageTotal.Resolve(page, items.Count, () => Convert.ToInt64(CachedQuery.Scalar(database, countSql, BindSearch) ?? 0));
        return new LibraryPageDto<LibraryGenreDto>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total,
            HasMore = page.Offset + items.Count < total,
        };
    }

    private static List<LibraryGenreDto> HydrateGenres(ReadOnlyLibraryDatabase database, List<string> keys)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var sql = $"""
            SELECT
              genre_key,
              display_name,
              track_count,
              album_count,
              cover_id,
              unclassified
            FROM local_genre_pages
            WHERE genre_key {SqlFragments.JsonEachIds}
            """;
        var byKey = new Dictionary<string, LibraryGenreDto>(keys.Count, StringComparer.Ordinal);
        using var reader = CachedQuery.Reader(database, sql, command =>
        {
            command.Add(System.Text.Json.JsonSerializer.Serialize(keys));
        });
        var mapper = GenreRowMapper.Bind(reader);
        while (reader.Read())
        {
            var genre = mapper.Map();
            byKey[genre.GenreKey] = genre;
        }

        var items = new List<LibraryGenreDto>(keys.Count);
        foreach (var key in keys)
        {
            if (byKey.TryGetValue(key, out var genre))
            {
                items.Add(genre);
            }
        }

        return items;
    }

    internal static string GenreOrderSql(string sort)
    {
        const string unclassifiedLast = "CASE WHEN unclassified != 0 OR genre_key = '__unclassified__' THEN 1 ELSE 0 END";
        const string nameAsc = "name_key COLLATE NOCASE ASC, name_text COLLATE NOCASE ASC, genre_key ASC";
        const string nameDesc = "name_key COLLATE NOCASE DESC, name_text COLLATE NOCASE DESC, genre_key ASC";
        return sort switch
        {
            "titleAsc" or "title" => $"ORDER BY {unclassifiedLast}, {nameAsc}",
            "titleDesc" => $"ORDER BY {unclassifiedLast}, {nameDesc}",
            "trackCountAsc" => $"ORDER BY {unclassifiedLast}, track_count ASC, {nameAsc}",
            "random" => "ORDER BY RANDOM()",
            _ => $"ORDER BY {unclassifiedLast}, track_count DESC, {nameAsc}",
        };
    }
}
