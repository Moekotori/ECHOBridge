namespace Echo.Core.Query;

internal static class AlbumVersionVisibility
{
    internal const string Sql = """

  NOT EXISTS (
    SELECT 1 FROM album_version_members AS own_version
    WHERE own_version.album_id = albums.id
      AND own_version.album_id != (
        SELECT candidate.album_id FROM album_version_members AS candidate
        INNER JOIN albums AS candidate_album ON candidate_album.id = candidate.album_id
        WHERE candidate.group_id = own_version.group_id
          AND EXISTS (SELECT 1 FROM album_tracks AS candidate_tracks
            INNER JOIN tracks AS candidate_track ON candidate_track.id = candidate_tracks.track_id
            WHERE candidate_tracks.album_id = candidate.album_id AND candidate_track.missing = 0 AND candidate_track.library_hidden = 0)
        ORDER BY candidate.is_primary DESC, candidate.album_id ASC LIMIT 1
      )
  )
""";
}
