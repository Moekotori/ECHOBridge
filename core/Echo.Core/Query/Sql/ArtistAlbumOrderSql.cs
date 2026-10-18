namespace Echo.Core.Query.Sql;

// Local artist-album pages follow albumOrderSql, which differs from the unified album wall.
internal static class ArtistAlbumOrderSql
{
    private const string Title = "sort_keys.title_key COLLATE NOCASE ASC, sort_keys.title_text COLLATE NOCASE ASC";
    private const string Artist = "sort_keys.artist_key COLLATE NOCASE ASC, sort_keys.artist_text COLLATE NOCASE ASC";
    private static string TrackAggregate(string expression) => $"""
        (SELECT {expression} FROM album_tracks
         INNER JOIN tracks ON tracks.id = album_tracks.track_id
         WHERE album_tracks.album_id = albums.id AND tracks.missing = 0 AND tracks.library_hidden = 0)
        """;

    internal static string Build(string sort, bool useTextKeys = true)
    {
        var title = useTextKeys ? Title : "albums.title COLLATE NOCASE ASC";
        var artist = useTextKeys ? Artist : "albums.album_artist COLLATE NOCASE ASC";
        var titleDesc = useTextKeys ? "sort_keys.title_key COLLATE NOCASE DESC, sort_keys.title_text COLLATE NOCASE DESC" : "albums.title COLLATE NOCASE DESC";
        var artistDesc = useTextKeys ? "sort_keys.artist_key COLLATE NOCASE DESC, sort_keys.artist_text COLLATE NOCASE DESC" : "albums.album_artist COLLATE NOCASE DESC";
        var order = sort switch
        {
            "artist" or "artistAlbum" => $"{artist}, {title}",
            "artistDesc" => $"{artistDesc}, {title}",
            "titleDesc" => $"{titleDesc}, {artist}",
            "recent" => $"COALESCE({TrackAggregate("MAX(tracks.created_at)")}, albums.created_at) DESC, albums.updated_at DESC, {title}",
            "yearAsc" => $"albums.year IS NULL, albums.year ASC, {title}",
            "yearDesc" => $"albums.year IS NULL, albums.year DESC, {title}",
            "createdAsc" => $"albums.created_at ASC, {title}",
            "createdDesc" => $"albums.created_at DESC, {title}",
            "durationAsc" => $"albums.duration ASC, {title}",
            "durationDesc" => $"albums.duration DESC, {title}",
            "fileModifiedAsc" => $"{TrackAggregate("MIN(tracks.mtime_ms)")} ASC, {title}",
            "fileModifiedDesc" => $"{TrackAggregate("MAX(tracks.mtime_ms)")} DESC, {title}",
            "random" => "RANDOM()",
            _ => $"{title}, {artist}",
        };
        return "ORDER BY " + order + (!useTextKeys && sort != "random" ? ", albums.id ASC" : "");
    }
}
