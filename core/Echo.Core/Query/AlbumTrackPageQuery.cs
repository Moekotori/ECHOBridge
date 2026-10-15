using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

public static class AlbumTrackPageQuery
{
    public static LibraryPageDto<LibraryTrackListRowDto> Execute(
        ReadOnlyLibraryDatabase database,
        string? albumId,
        CoreLibraryPageQuery? query)
    {
        if (!QueryCapabilities.SupportsAlbumTracks(albumId, query))
        {
            throw new UnsupportedQueryException("getAlbumTracks combination is not migrated.");
        }

        var page = PageQueryNormalizer.Normalize(query);
        var list = QueryCapabilities.IsListProjection(query);
        const string countSql = """
            SELECT COUNT(*)
            FROM album_tracks
            INNER JOIN tracks ON tracks.id = album_tracks.track_id
            WHERE album_tracks.album_id = ? AND tracks.missing = 0 AND tracks.library_hidden = 0
            """;
        const string idSql = """
            SELECT tracks.id, COALESCE(album_tracks.disc_no, tracks.disc_no) AS index_disc_no
            FROM album_tracks
            INNER JOIN tracks ON tracks.id = album_tracks.track_id
            WHERE album_tracks.album_id = ? AND tracks.missing = 0 AND tracks.library_hidden = 0
            ORDER BY
              COALESCE(album_tracks.disc_no, tracks.disc_no, 1),
              CASE WHEN COALESCE(album_tracks.track_no, tracks.track_no) IS NULL THEN 1 ELSE 0 END,
              COALESCE(album_tracks.track_no, tracks.track_no),
              tracks.title COLLATE NOCASE,
              album_tracks.position ASC
            LIMIT ? OFFSET ?
            """;

        using var transaction = database.BeginDeferredRead();
        var total = Convert.ToInt64(CachedQuery.Scalar(database, countSql, command => command.Add(albumId)) ?? 0);
        var ids = ReadIds(database, idSql, command =>
        {
            command.Add(albumId);
            command.Add(page.PageSize);
            command.Add(page.Offset);
        }, out var indexedDiscs);
        var items = TrackPageQuery.HydrateTracks(database, ids, list);
        foreach (var item in items)
            if (indexedDiscs.TryGetValue(item.Id, out var disc)) item.DiscNo = disc;
        return new LibraryPageDto<LibraryTrackListRowDto>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total,
            HasMore = page.Offset + items.Count < total,
        };
    }

    private static List<string> ReadIds(ReadOnlyLibraryDatabase database, string sql, Action<SqliteCommand> bind, out Dictionary<string, int> indexedDiscs)
    {
        var ids = new List<string>();
        indexedDiscs = new(StringComparer.Ordinal);
        using var reader = CachedQuery.Reader(database, sql, bind);
        while (reader.Read())
        {
            var id = Convert.ToString(reader[0]) ?? string.Empty;
            ids.Add(id);
            if (!reader.IsDBNull(1)) indexedDiscs[id] = Convert.ToInt32(reader[1]);
        }

        return ids;
    }
}
