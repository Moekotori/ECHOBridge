using Echo.Core.Protocol;

namespace Echo.Core.Query;

internal static class FavoriteItemsSql
{
    public static string Order(string? sort, bool useTextKeys = true) => sort switch
    {
        null or "" or "default" or "manual" => "ORDER BY playlist_items.position ASC, playlist_items.added_at ASC",
        "recent" or "addedDesc" => "ORDER BY playlist_items.added_at DESC, playlist_items.position ASC",
        "addedAsc" => "ORDER BY playlist_items.added_at ASC, playlist_items.position ASC",
        "frequent" => """
            ORDER BY CASE WHEN playlist_items.media_type='album' THEN
              COALESCE((SELECT SUM(COALESCE(t.play_count,0)) FROM album_tracks a
                INNER JOIN tracks t ON t.id=a.track_id WHERE a.album_id=playlist_items.media_id AND t.missing=0 AND t.library_hidden=0),0)
              ELSE COALESCE(tracks.play_count,0) END DESC,
              CASE WHEN playlist_items.media_type='album' THEN
              (SELECT MAX(t.last_played_at) FROM album_tracks a INNER JOIN tracks t ON t.id=a.track_id
                WHERE a.album_id=playlist_items.media_id AND t.missing=0 AND t.library_hidden=0)
              ELSE tracks.last_played_at END DESC,
              playlist_items.added_at DESC,playlist_items.position ASC
            """,
        "qualityAsc" or "qualityDesc" => Quality(sort == "qualityAsc" ? "ASC" : "DESC", useTextKeys),
        "audioSpecAsc" or "audioSpecDesc" => AudioSpec(sort == "audioSpecAsc" ? "ASC" : "DESC", useTextKeys),
        _ => PlaylistSnapshotOrderSql.For(sort, useTextKeys),
    };

    private static string Quality(string direction, bool useTextKeys) => $"ORDER BY COALESCE(tracks.bitrate,0) {direction}, " +
        $"COALESCE(tracks.size_bytes,0) {direction}, {PlaylistSnapshotOrderSql.Text("title", useTextKeys: useTextKeys)}, playlist_items.position ASC";

    private static string AudioSpec(string direction, bool useTextKeys) => "ORDER BY (tracks.sample_rate IS NULL OR tracks.sample_rate<=0) ASC, " +
        $"tracks.sample_rate {direction}, (tracks.bit_depth IS NULL OR tracks.bit_depth<=0) ASC, " +
        $"tracks.bit_depth {direction}, {PlaylistSnapshotOrderSql.Text("title", useTextKeys: useTextKeys)}, playlist_items.position ASC";

    public static string Where(bool albums, string? sourceProvider, List<object?> parameters)
    {
        var sql = albums ? " AND playlist_items.media_type='album'" : " AND playlist_items.media_type IN ('track','stream_track')";
        if (sourceProvider == "local") sql += " AND playlist_items.source_provider IN ('local','remote')";
        else if (!string.IsNullOrEmpty(sourceProvider))
        {
            sql += " AND playlist_items.source_provider=?";
            parameters.Add(sourceProvider);
        }
        return sql + """
             AND (playlist_items.source_provider!='remote' OR EXISTS (
              SELECT 1 FROM remote_tracks INNER JOIN remote_sources ON remote_sources.id=remote_tracks.source_id
              WHERE remote_tracks.id=playlist_items.media_id AND remote_sources.status='enabled'))
            """;
    }
}
