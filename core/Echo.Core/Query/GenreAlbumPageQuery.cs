using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class GenreAlbumPageQuery
{
    public static LibraryPageDto<LibraryAlbumDto> Execute(
        ReadOnlyLibraryDatabase database,
        string? genreKey,
        CoreLibraryPageQuery? query)
    {
        if (!QueryCapabilities.SupportsGenreAlbums(genreKey, query))
        {
            throw new UnsupportedQueryException("getGenreAlbums combination is not migrated.");
        }

        var page = PageQueryNormalizer.Normalize(query);
        var normalizedKey = GenreKey.IsUnclassified(genreKey) ? GenreKey.Unclassified : GenreKey.FromTag(genreKey);
        var orderSql = OrderSql(QueryCapabilities.PrimarySort(query));
        var countSql = """
            SELECT COUNT(DISTINCT albums.id)
            FROM albums
            INNER JOIN album_tracks ON album_tracks.album_id = albums.id
            INNER JOIN tracks ON tracks.id = album_tracks.track_id
            WHERE tracks.missing = 0 AND tracks.library_hidden = 0
              AND echo_genre_key(tracks.genre) = ?
            """;
        var idSql = $"""
            SELECT albums.id
            FROM albums
            WHERE albums.id IN (
              SELECT DISTINCT album_tracks.album_id
              FROM album_tracks
              INNER JOIN tracks ON tracks.id = album_tracks.track_id
              WHERE tracks.missing = 0 AND tracks.library_hidden = 0
                AND echo_genre_key(tracks.genre) = ?
            )
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

        var items = AlbumPageQuery.HydrateAlbums(database, ids);
        return new LibraryPageDto<LibraryAlbumDto>
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
        "titleDesc" => "ORDER BY albums.title COLLATE NOCASE DESC, albums.id ASC",
        "yearDesc" => "ORDER BY albums.year IS NULL, albums.year DESC, albums.title COLLATE NOCASE ASC, albums.id ASC",
        _ => "ORDER BY albums.title COLLATE NOCASE ASC, albums.id ASC",
    };
}
