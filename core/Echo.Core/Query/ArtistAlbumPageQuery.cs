using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class ArtistAlbumPageQuery
{
    public static LibraryPageDto<LibraryAlbumDto> Execute(
        ReadOnlyLibraryDatabase database, string? artistId, CoreLibraryPageQuery? query, bool allowUnindexedTextSort = false,
        CoreNormalizedEntitySearch? search = null)
    {
        if (!QueryCapabilities.SupportsArtistAlbums(artistId, query))
            throw new UnsupportedQueryException("getArtistAlbums requires a local split artist.");
        SearchContract.EnsureExecutableEntity(query, search);
        var filter = SqlFragments.LikeEntityFilter(["albums.title", "albums.album_artist"], search);
        var filterSql = string.IsNullOrEmpty(filter.Sql) ? string.Empty : $" AND {filter.Sql}";
        var visibilitySql = $" AND {VisibleAlbumSql.Predicate}";
        void Bind(Microsoft.Data.Sqlite.SqliteCommand command)
        {
            command.Add(artistId);
            foreach (var value in filter.Parameters) command.Add(value);
        }

        // This existing endpoint accepts page/pageSize/sort only, not offset or multi-sort.
        var page = PageQueryNormalizer.Normalize(new CoreLibraryPageQuery { Page = query?.Page, PageSize = query?.PageSize });
        var sort = query?.Sort ?? "default";
        var useTextKeys = sort != "random" && (!allowUnindexedTextSort || PersistedSortKeys.UseTextKeys(database,
            PersistedSortKeys.AreAlbumKeysFresh(database), true, "local_album_sort_keys are stale."));
        using var transaction = database.BeginDeferredRead();
        var total = Convert.ToInt64(CachedQuery.Scalar(database, """
            SELECT COUNT(*) FROM artist_albums
            INNER JOIN albums ON albums.id = artist_albums.album_id
            WHERE artist_albums.artist_id = ?
            """ + visibilitySql + filterSql, Bind) ?? 0);
        var ids = new List<string>();
        if (total > page.Offset)
        {
            if (sort != "random" && !allowUnindexedTextSort && !PersistedSortKeys.AreAlbumKeysFresh(database))
                throw new UnsupportedQueryException("local_album_sort_keys are stale.");
            var join = !useTextKeys ? string.Empty
                : "INNER JOIN local_album_sort_keys AS sort_keys ON sort_keys.album_id = albums.id";
            var sql = $"""
                SELECT albums.id FROM artist_albums
                INNER JOIN albums ON albums.id = artist_albums.album_id
                {join}
                WHERE artist_albums.artist_id = ? {visibilitySql} {filterSql}
                {ArtistAlbumOrderSql.Build(sort, useTextKeys)}
                LIMIT ? OFFSET ?
                """;
            using var reader = CachedQuery.Reader(database, sql, command =>
            {
                Bind(command);
                command.Add(page.PageSize);
                command.Add(page.Offset);
            });
            while (reader.Read()) ids.Add(reader.GetString(0));
        }
        var items = AlbumPageQuery.HydrateAlbums(database, ids);
        return new LibraryPageDto<LibraryAlbumDto>
        {
            Items = items, Page = page.Page, PageSize = page.PageSize,
            Total = total, HasMore = page.Offset + items.Count < total,
        };
    }
}
