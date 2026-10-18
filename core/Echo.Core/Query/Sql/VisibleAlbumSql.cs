namespace Echo.Core.Query.Sql;

internal static class VisibleAlbumSql
{
    internal const string Predicate = """
        EXISTS (
          SELECT 1 FROM album_tracks
          INNER JOIN tracks ON tracks.id=album_tracks.track_id
          WHERE album_tracks.album_id=albums.id AND tracks.missing=0 AND tracks.library_hidden=0
        )
        """;
}
