using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

internal static class PersistedSortKeys
{
    // Standalone read-only clients cannot ask the desktop writer to refresh an index.
    // Sorting source text in SQLite keeps their pages available without changing the library.
    public static bool UseTextKeys(ReadOnlyLibraryDatabase database, bool fresh, bool allowUnindexed, string message)
    {
        if (fresh) return true;
        if (!allowUnindexed) throw new UnsupportedQueryException(message);
        using var command = database.RequireConnection().CreateCommand();
        command.CommandText = "PRAGMA temp_store=FILE";
        command.ExecuteNonQuery();
        return false;
    }

    public static bool AreFresh(
        ReadOnlyLibraryDatabase database,
        string revisionTable,
        string flagKey,
        string format = ProtocolConstants.SortKeyFormat)
    {
        var revision = Convert.ToString(Scalar(database, $"SELECT revision FROM {revisionTable} WHERE id = 1") ?? 0);
        var expected = $"{format}|rev:{revision}";
        var persisted = Convert.ToString(Scalar(
            database,
            "SELECT value FROM library_maintenance_flags WHERE key = ?",
            command => command.Add(flagKey)));
        return persisted == expected;
    }

    public static bool AreTrackKeysFresh(ReadOnlyLibraryDatabase database) =>
        AreFresh(database, "local_track_sort_key_revision", ProtocolConstants.SyncedRevisionFlagKey);

    public static bool AreAlbumKeysFresh(ReadOnlyLibraryDatabase database) =>
        AreFresh(database, "local_album_sort_key_revision", ProtocolConstants.AlbumSyncedRevisionFlagKey);

    public static bool AreArtistKeysFresh(ReadOnlyLibraryDatabase database) =>
        AreFresh(database, "local_artist_sort_key_revision", ProtocolConstants.ArtistSyncedRevisionFlagKey);

    public static bool AreGenrePagesFresh(ReadOnlyLibraryDatabase database) =>
        AreFresh(
            database,
            "local_genre_page_revision",
            ProtocolConstants.GenrePageSyncedRevisionFlagKey,
            ProtocolConstants.GenrePageFormat);

    public static long? TryTrackPresentCount(ReadOnlyLibraryDatabase database)
    {
        if (!AreTrackKeysFresh(database))
        {
            return null;
        }

        var persisted = Convert.ToString(Scalar(
            database,
            "SELECT value FROM library_maintenance_flags WHERE key = ?",
            command => command.Add(ProtocolConstants.TrackPresentCountFlagKey)));
        return long.TryParse(persisted, out var count) && count >= 0 ? count : null;
    }

    private static object? Scalar(ReadOnlyLibraryDatabase database, string sql, Action<Microsoft.Data.Sqlite.SqliteCommand>? bind = null)
    {
        return CachedQuery.Scalar(database, sql, bind);
    }
}
