namespace Echo.Core.Query;

// Identical key + original-text ordering to Electron's libraryTextOrderSql.
internal static class PlaylistSnapshotOrderSql
{
    public static bool NeedsKeys(string? sort) => sort is "title" or "titleAsc" or "titleDesc"
        or "artist" or "artistAsc" or "artistDesc" or "artistAlbum" or "album" or "albumDesc"
        or "durationAsc" or "durationDesc" or "qualityAsc" or "qualityDesc" or "audioSpecAsc" or "audioSpecDesc";

    public static string Text(string field, string direction = "ASC") =>
        $"playlist_keys.{field}_key COLLATE NOCASE {direction}, playlist_keys.{field}_text COLLATE NOCASE {direction}";

    public static string For(string? sort)
    {
        var title = Text("title");
        var duration = "COALESCE(playlist_items.duration_snapshot, tracks.duration, albums.duration, 0)";
        var expression = sort switch
        {
            "title" or "titleAsc" => title,
            "titleDesc" => Text("title", "DESC"),
            "artist" or "artistAsc" => $"{Text("artist")}, {title}",
            "artistDesc" => $"{Text("artist", "DESC")}, {title}",
            "artistAlbum" => $"{Text("artist")}, {Text("album")}, {title}",
            "album" => $"{Text("album")}, {title}",
            "albumDesc" => $"{Text("album", "DESC")}, {title}",
            "durationAsc" => $"{duration} ASC, {title}",
            "durationDesc" => $"{duration} DESC, {title}",
            _ => null,
        };
        return expression is null ? PlaylistItemsOrderSql.For(sort) : $"ORDER BY {expression}, playlist_items.position ASC";
    }
}
