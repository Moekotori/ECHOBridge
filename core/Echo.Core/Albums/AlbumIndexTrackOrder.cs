using Echo.Core.Database;

namespace Echo.Core.Albums;

internal static class AlbumIndexTrackOrder
{
    public static void Apply(TransactionSql sql, bool affectedOnly = false) => sql.Run("""
        WITH ranked AS (
          SELECT album_tracks.album_id,album_tracks.track_id,
            ROW_NUMBER() OVER(PARTITION BY album_tracks.album_id ORDER BY
              COALESCE(album_tracks.disc_no,tracks.disc_no,1),
              COALESCE(album_tracks.track_no,tracks.track_no) IS NULL,
              COALESCE(album_tracks.track_no,tracks.track_no),tracks.title COLLATE NOCASE,
              album_tracks.position,album_tracks.track_id)-1 AS next_position
          FROM album_tracks JOIN tracks ON tracks.id=album_tracks.track_id
        """ + (affectedOnly ? " WHERE album_tracks.album_id IN(SELECT id FROM ag_affected)" : "") + """
        ) UPDATE album_tracks SET position=ranked.next_position FROM ranked
          WHERE album_tracks.album_id=ranked.album_id AND album_tracks.track_id=ranked.track_id
            AND album_tracks.position IS NOT ranked.next_position
        """);
}
