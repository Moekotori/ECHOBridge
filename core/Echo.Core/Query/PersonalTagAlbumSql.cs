namespace Echo.Core.Query;

internal static class PersonalTagAlbumSql
{
    internal const string Sql = """
EXISTS (
  SELECT 1 FROM personal_tag_items AS tagged WHERE tagged.tag_id = ? AND (
    (tagged.kind = 'album' AND tagged.target_id = albums.id)
    OR (tagged.kind = 'track' AND EXISTS (SELECT 1 FROM album_tracks
      WHERE album_tracks.album_id = albums.id AND album_tracks.track_id = tagged.target_id))
    OR (tagged.kind = 'artist' AND tagged.target_id LIKE 'album-artist:%'
      AND albums.album_artist = tagged.title COLLATE NOCASE)
    OR (tagged.kind = 'artist' AND EXISTS (SELECT 1 FROM artist_albums
      WHERE artist_albums.album_id = albums.id AND artist_albums.artist_id = tagged.target_id))
  )
)
""";
}
