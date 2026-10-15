using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class PlaylistCoverQuery
{
    public static void Enrich(ReadOnlyLibraryDatabase database, LibraryPlaylistDto playlist)
    {
        // CoverId remains the persisted user choice; automatic artwork is presentation only.
        if (playlist.Kind is "smart" or "system" || playlist.CoverId is not null || playlist.CoverThumb is not null) return;
        var sql = """
            SELECT CASE WHEN tracks.id IS NOT NULL THEN tracks.cover_id
                   ELSE COALESCE(playlist_items.cover_id, albums.cover_id) END, streaming_tracks.cover_url
            FROM playlist_items
            LEFT JOIN tracks ON tracks.id = playlist_items.media_id
            LEFT JOIN albums ON albums.id = playlist_items.media_id
            LEFT JOIN streaming_tracks
              ON streaming_tracks.provider = playlist_items.source_provider
             AND streaming_tracks.provider_track_id = playlist_items.source_item_id
            WHERE playlist_items.playlist_id = ?
            """ + " " + PlaylistItemsOrderSql.For(playlist.SortMode) + " LIMIT 1";
        using var reader = CachedQuery.Reader(database, sql, command => command.Add(playlist.Id));
        if (!reader.Read()) return;
        playlist.CoverThumb = !reader.IsDBNull(0)
            ? LibraryText.CoverUrl(reader.GetString(0), "album")
            : reader.IsDBNull(1) ? null : reader.GetString(1);
    }
}
