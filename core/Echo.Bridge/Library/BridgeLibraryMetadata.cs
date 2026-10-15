using Echo.Core.Database;

namespace Echo.Bridge.Library;

/// <summary>Small read models for configuration/asset fields not exposed by the Core query facade.</summary>
public static class BridgeLibraryMetadata
{
    public static T Read<T>(BridgeProfile profile, CancellationToken cancellation, Func<ReadOnlyLibraryDatabase, T> query)
    {
        cancellation.ThrowIfCancellationRequested();
        using var database = new ReadOnlyLibraryDatabase(budgetParts: 1);
        database.Open(profile.DatabasePath, 1);
        using var interrupt = cancellation.Register(database.InterruptReads);
        var result = query(database);
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    public static string? FavoritesId(BridgeProfile profile, CancellationToken cancellation) => Read(profile, cancellation, database =>
    {
        using var command = database.RequireConnection().CreateCommand();
        command.CommandText = "SELECT id FROM playlists WHERE kind='system' AND source_provider='local' AND source_playlist_id='liked-tracks' LIMIT 1";
        return command.ExecuteScalar() as string;
    });

    public static string? Cover(BridgeProfile profile, string id, CancellationToken cancellation, int size = 160) => Read(profile, cancellation, database =>
    {
        using var command = database.RequireConnection().CreateCommand();
        command.CommandText = "SELECT COALESCE(thumb_path,cover_thumb),album_path,COALESCE(large_path,cover_large) FROM covers WHERE id=$id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);
        using var row = command.ExecuteReader();
        if (!row.Read()) return null;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.CoverDirectory)) + Path.DirectorySeparatorChar;
        foreach (var column in size > 320 ? new[] { 2, 1, 0 } : new[] { 1, 0, 2 })
        {
            if (row.IsDBNull(column) || string.IsNullOrWhiteSpace(row.GetString(column))) continue;
            var full = Path.GetFullPath(row.GetString(column), profile.CoverDirectory);
            if (full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && File.Exists(full)) return full;
        }
        return null;
    });
}
