namespace Echo.Core.Query;

public static class PlaylistItemsOrderSql
{
    private const string TitleSql = "COALESCE(playlist_items.title_snapshot, tracks.title, albums.title, '')";
    private const string ArtistSql = "COALESCE(playlist_items.artist_snapshot, tracks.artist, albums.album_artist, '')";
    private const string AlbumSql = "COALESCE(playlist_items.album_snapshot, tracks.album, albums.title, '')";
    private const string DurationSql = "COALESCE(playlist_items.duration_snapshot, tracks.duration, albums.duration, 0)";

    public static string For(string? sort) =>
        sort switch
        {
            "addedDesc" or "recent" => "ORDER BY playlist_items.added_at DESC, playlist_items.position ASC",
            "addedAsc" => "ORDER BY playlist_items.added_at ASC, playlist_items.position ASC",
            "titleDesc" => $"ORDER BY {TitleSql} COLLATE NOCASE DESC, playlist_items.position ASC",
            "titleAsc" or "title" => $"ORDER BY {TitleSql} COLLATE NOCASE ASC, playlist_items.position ASC",
            "artistDesc" => $"ORDER BY {ArtistSql} COLLATE NOCASE DESC, {TitleSql} COLLATE NOCASE ASC, playlist_items.position ASC",
            "artistAsc" or "artist" => $"ORDER BY {ArtistSql} COLLATE NOCASE ASC, {TitleSql} COLLATE NOCASE ASC, playlist_items.position ASC",
            "artistAlbum" => $"ORDER BY {ArtistSql} COLLATE NOCASE ASC, {AlbumSql} COLLATE NOCASE ASC, {TitleSql} COLLATE NOCASE ASC, playlist_items.position ASC",
            "album" => $"ORDER BY {AlbumSql} COLLATE NOCASE ASC, {TitleSql} COLLATE NOCASE ASC, playlist_items.position ASC",
            "albumDesc" => $"ORDER BY {AlbumSql} COLLATE NOCASE DESC, {TitleSql} COLLATE NOCASE ASC, playlist_items.position ASC",
            "durationAsc" => $"ORDER BY {DurationSql} ASC, {TitleSql} COLLATE NOCASE ASC, playlist_items.position ASC",
            "durationDesc" => $"ORDER BY {DurationSql} DESC, {TitleSql} COLLATE NOCASE ASC, playlist_items.position ASC",
            _ => "ORDER BY playlist_items.position ASC, playlist_items.added_at ASC",
        };
}
