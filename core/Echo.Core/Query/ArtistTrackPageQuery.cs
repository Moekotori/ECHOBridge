using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class ArtistTrackPageQuery
{
    public static LibraryPageDto<LibraryTrackListRowDto> Execute(
        ReadOnlyLibraryDatabase database,
        string? artistId,
        CoreLibraryPageQuery? query,
        CoreSearchOptions? searchOptions)
    {
        if (!QueryCapabilities.SupportsArtistTracks(artistId, query))
        {
            throw new UnsupportedQueryException("getArtistTracks combination is not migrated.");
        }

        var page = PageQueryNormalizer.Normalize(query);
        var list = QueryCapabilities.IsListProjection(query);
        using var transaction = database.BeginDeferredRead();
        if (ReadLocalArtist(database, artistId!) is not { } artist)
        {
            throw new UnsupportedQueryException("getArtistTracks requires a local artists-table row.");
        }

        const string countSql = """
            SELECT COUNT(*) FROM artist_tracks
            INNER JOIN tracks ON tracks.id = artist_tracks.track_id
            WHERE artist_tracks.artist_id = ?
              AND tracks.missing = 0 AND tracks.library_hidden = 0
            """;
        var idSql = $"""
            SELECT tracks.id FROM artist_tracks
            INNER JOIN tracks ON tracks.id = artist_tracks.track_id
            WHERE artist_tracks.artist_id = ?
              AND tracks.missing = 0 AND tracks.library_hidden = 0
            {OrderSql(page.Sorts[0])}
            LIMIT ? OFFSET ?
            """;
        var total = Convert.ToInt64(CachedQuery.Scalar(database, countSql, command =>
            command.Add(artist.Id)) ?? 0);
        var ids = new List<string>();
        using (var reader = CachedQuery.Reader(database, idSql, command =>
        {
            command.Add(artist.Id);
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

    private static LocalArtist? ReadLocalArtist(ReadOnlyLibraryDatabase database, string artistId)
    {
        const string sql = "SELECT id, name, artist_key FROM artists WHERE id = ?";
        using var reader = CachedQuery.Reader(database, sql, command => command.Add(artistId));
        if (!reader.Read())
        {
            return null;
        }

        return new LocalArtist(
            Convert.ToString(reader[0]) ?? string.Empty,
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            reader.IsDBNull(2) ? string.Empty : reader.GetString(2));
    }

    private static string OrderSql(string sort) => sort switch
    {
        "recent" => "ORDER BY COALESCE(tracks.last_played_at, tracks.updated_at) DESC, tracks.title COLLATE NOCASE ASC",
        "frequent" => "ORDER BY COALESCE(tracks.play_count, 0) DESC, tracks.last_played_at DESC, tracks.title COLLATE NOCASE ASC",
        "title" or "titleAsc" => "ORDER BY tracks.title COLLATE NOCASE ASC, tracks.album COLLATE NOCASE ASC",
        "titleDesc" => "ORDER BY tracks.title COLLATE NOCASE DESC, tracks.album COLLATE NOCASE ASC",
        "durationAsc" => "ORDER BY tracks.duration ASC, tracks.title COLLATE NOCASE ASC",
        "durationDesc" => "ORDER BY tracks.duration DESC, tracks.title COLLATE NOCASE ASC",
        "fileModifiedAsc" => "ORDER BY tracks.mtime_ms ASC, tracks.title COLLATE NOCASE ASC",
        "fileModifiedDesc" => "ORDER BY tracks.mtime_ms DESC, tracks.title COLLATE NOCASE ASC",
        "random" => "ORDER BY RANDOM()",
        _ => "ORDER BY tracks.album COLLATE NOCASE ASC, COALESCE(tracks.disc_no, 0), COALESCE(tracks.track_no, 0), tracks.title COLLATE NOCASE ASC",
    };

    private readonly record struct LocalArtist(string Id, string Name, string ArtistKey);
}
