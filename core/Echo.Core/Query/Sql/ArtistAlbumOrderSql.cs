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

    internal static string Build(string sort)
    {
        var order = sort switch
        {
            "artist" or "artistAlbum" => $"{Artist}, {Title}",
            "artistDesc" => $"sort_keys.artist_key COLLATE NOCASE DESC, sort_keys.artist_text COLLATE NOCASE DESC, {Title}",
            "titleDesc" => $"sort_keys.title_key COLLATE NOCASE DESC, sort_keys.title_text COLLATE NOCASE DESC, {Artist}",
            "recent" => $"COALESCE({TrackAggregate("MAX(tracks.created_at)")}, albums.created_at) DESC, albums.updated_at DESC, {Title}",
            "yearAsc" => $"albums.year IS NULL, albums.year ASC, {Title}",
            "yearDesc" => $"albums.year IS NULL, albums.year DESC, {Title}",
            "createdAsc" => $"albums.created_at ASC, {Title}",
            "createdDesc" => $"albums.created_at DESC, {Title}",
            "durationAsc" => $"albums.duration ASC, {Title}",
            "durationDesc" => $"albums.duration DESC, {Title}",
            "fileModifiedAsc" => $"{TrackAggregate("MIN(tracks.mtime_ms)")} ASC, {Title}",
            "fileModifiedDesc" => $"{TrackAggregate("MAX(tracks.mtime_ms)")} DESC, {Title}",
            "random" => "RANDOM()",
            _ => $"{Title}, {Artist}",
        };
        return "ORDER BY " + order;
    }
}
