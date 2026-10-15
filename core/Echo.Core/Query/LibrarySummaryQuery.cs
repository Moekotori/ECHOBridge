using Echo.Core.Database;
using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

public static class LibrarySummaryQuery
{
    public static LibrarySummaryDto Execute(ReadOnlyLibraryDatabase database)
    {
        var connection = database.RequireConnection();
        return new LibrarySummaryDto
        {
            SongCount = Scalar(connection, "SELECT COUNT(*) FROM tracks WHERE missing = 0 AND library_hidden = 0"),
            AlbumCount = Scalar(connection, """
                SELECT COUNT(*) FROM albums
                WHERE EXISTS (
                  SELECT 1 FROM album_tracks
                  INNER JOIN tracks ON tracks.id = album_tracks.track_id
                  WHERE album_tracks.album_id = albums.id
                    AND tracks.missing = 0 AND tracks.library_hidden = 0
                )
                """),
            ArtistCount = Scalar(connection, """
                SELECT COUNT(*) FROM artists
                WHERE EXISTS (
                  SELECT 1 FROM artist_tracks
                  INNER JOIN tracks ON tracks.id = artist_tracks.track_id
                  WHERE artist_tracks.artist_id = artists.id
                    AND tracks.missing = 0 AND tracks.library_hidden = 0
                )
                """),
            FolderCount = Scalar(connection, "SELECT COUNT(*) FROM folders WHERE enabled = 1 AND status != 'removed'"),
            TotalDuration = Convert.ToDouble(ScalarObject(connection, "SELECT COALESCE(SUM(duration), 0) FROM tracks WHERE missing = 0 AND library_hidden = 0") ?? 0),
            LastScanAt = ScalarObject(connection, "SELECT finished_at FROM scan_jobs WHERE finished_at IS NOT NULL ORDER BY finished_at DESC LIMIT 1") is string lastScan
                && lastScan.Length > 0
                    ? lastScan
                    : null,
        };
    }

    private static long Scalar(SqliteConnection connection, string sql) =>
        Convert.ToInt64(ScalarObject(connection, sql) ?? 0);

    private static object? ScalarObject(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
