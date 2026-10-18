namespace Echo.Core.Query;

// Identical key + original-text ordering to Electron's libraryTextOrderSql.
internal static class PlaylistSnapshotOrderSql
{
    public static bool NeedsKeys(string? sort) => sort is "title" or "titleAsc" or "titleDesc"
        or "artist" or "artistAsc" or "artistDesc" or "artistAlbum" or "album" or "albumDesc"
        or "durationAsc" or "durationDesc" or "qualityAsc" or "qualityDesc" or "audioSpecAsc" or "audioSpecDesc";

    public static string Text(string field, string direction = "ASC", bool useTextKeys = true) => useTextKeys
        ? $"playlist_keys.{field}_key COLLATE NOCASE {direction}, playlist_keys.{field}_text COLLATE NOCASE {direction}"
        : $"COALESCE(playlist_items.{field}_snapshot, tracks.{field}, albums.{(field == "artist" ? "album_artist" : "title")}, '') COLLATE NOCASE {direction}";

    public static string For(string? sort, bool useTextKeys = true)
    {
        string Key(string field, string direction = "ASC") => Text(field, direction, useTextKeys);
        var title = Key("title");
        var duration = "COALESCE(playlist_items.duration_snapshot, tracks.duration, albums.duration, 0)";
        var expression = sort switch
        {
            "title" or "titleAsc" => title,
            "titleDesc" => Key("title", "DESC"),
            "artist" or "artistAsc" => $"{Key("artist")}, {title}",
            "artistDesc" => $"{Key("artist", "DESC")}, {title}",
            "artistAlbum" => $"{Key("artist")}, {Key("album")}, {title}",
            "album" => $"{Key("album")}, {title}",
            "albumDesc" => $"{Key("album", "DESC")}, {title}",
            "durationAsc" => $"{duration} ASC, {title}",
            "durationDesc" => $"{duration} DESC, {title}",
            _ => null,
        };
        return expression is null ? PlaylistItemsOrderSql.For(sort) : $"ORDER BY {expression}, playlist_items.position ASC";
    }
}
