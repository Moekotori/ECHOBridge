using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class GenreTrackPageQuery
{
    public static LibraryPageDto<LibraryTrackListRowDto> Execute(
        ReadOnlyLibraryDatabase database,
        string? genreKey,
        CoreLibraryPageQuery? query)
    {
        if (!QueryCapabilities.SupportsGenreTracks(genreKey, query))
        {
            throw new UnsupportedQueryException("getGenreTracks combination is not migrated.");
        }

        var page = PageQueryNormalizer.Normalize(query);
        var list = QueryCapabilities.IsListProjection(query);
        var normalizedKey = GenreKey.IsUnclassified(genreKey) ? GenreKey.Unclassified : GenreKey.FromTag(genreKey);
        var orderSql = OrderSql(QueryCapabilities.PrimarySort(query));
        var countSql = """
            SELECT COUNT(*)
            FROM tracks
            WHERE tracks.missing = 0 AND tracks.library_hidden = 0
              AND echo_genre_key(tracks.genre) = ?
            """;
        var idSql = $"""
            SELECT tracks.id
            FROM tracks
            WHERE tracks.missing = 0 AND tracks.library_hidden = 0
              AND echo_genre_key(tracks.genre) = ?
            {orderSql}
            LIMIT ? OFFSET ?
            """;

        using var transaction = database.BeginDeferredRead();
        var total = Convert.ToInt64(CachedQuery.Scalar(database, countSql, command => command.Add(normalizedKey)) ?? 0);
        var ids = new List<string>();
        using (var reader = CachedQuery.Reader(database, idSql, command =>
        {
            command.Add(normalizedKey);
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
        return new LibraryPageDto<LibraryTrackListRowDto>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total,
            HasMore = page.Offset + items.Count < total,
        };
    }

    private static string OrderSql(string sort) => sort switch
    {
        "title" or "titleAsc" => "ORDER BY tracks.title COLLATE NOCASE ASC",
        "titleDesc" => "ORDER BY tracks.title COLLATE NOCASE DESC",
        "durationDesc" => "ORDER BY tracks.duration DESC, tracks.title COLLATE NOCASE ASC",
        "durationAsc" => "ORDER BY tracks.duration ASC, tracks.title COLLATE NOCASE ASC",
        "random" => "ORDER BY RANDOM()",
        _ => "ORDER BY tracks.album COLLATE NOCASE ASC, tracks.disc_no ASC, tracks.track_no ASC, tracks.title COLLATE NOCASE ASC",
    };
}
