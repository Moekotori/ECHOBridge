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

    public static (List<Dictionary<string, object?>> Entries, long Total) Folders(BridgeProfile profile, string search, int page, CancellationToken cancellation)
        => Read(profile, cancellation, database =>
        {
            using var command = database.RequireConnection().CreateCommand();
            command.CommandText = "SELECT id,name FROM folders WHERE enabled=1 AND status!='removed' ORDER BY created_at,id";
            using var rows = command.ExecuteReader();
            var entries = new List<Dictionary<string, object?>>(50);
            long total = 0;
            while (rows.Read())
            {
                if (!rows.GetString(1).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (total >= (page - 1L) * 50 && entries.Count < 50)
                    entries.Add(BridgeTrackProjection.Container("folder", "root:" + rows.GetString(0), rows.GetString(1), "", 0, null));
                total++;
            }
            return (entries, total);
        });

    public static string? Cover(BridgeProfile profile, string id, CancellationToken cancellation) => Read(profile, cancellation, database =>
    {
        using var command = database.RequireConnection().CreateCommand();
        command.CommandText = "SELECT COALESCE(thumb_path,cover_thumb,album_path) FROM covers WHERE id=$id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);
        var path = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(path)) return null;
        var full = Path.GetFullPath(path, profile.CoverDirectory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.CoverDirectory)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && File.Exists(full) ? full : null;
    });
}
