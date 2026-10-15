namespace Echo.Core.Query.Sql;

internal static class AlbumDisplayCoverSql
{
    // Only hydrate artwork for the selected album IDs. Each fallback stays within
    // the album_tracks album_id index and returns one visible track's real cover.
    public const string Joins = """
        LEFT JOIN covers AS stored_album_cover ON stored_album_cover.id = albums.cover_id
        LEFT JOIN covers ON covers.id = COALESCE(
          CASE WHEN stored_album_cover.source_type = 'default' THEN NULL ELSE albums.cover_id END,
          (
            SELECT track_cover.id
            FROM album_tracks AS cover_members
            INNER JOIN tracks AS cover_track ON cover_track.id = cover_members.track_id
            INNER JOIN covers AS track_cover ON track_cover.id = cover_track.cover_id
            WHERE cover_members.album_id = albums.id
              AND cover_track.missing = 0 AND cover_track.library_hidden = 0
              AND track_cover.source_type != 'default'
            ORDER BY cover_members.position ASC
            LIMIT 1
          ),
          albums.cover_id
        )
        """;
}
