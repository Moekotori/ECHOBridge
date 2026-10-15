namespace Echo.Core.Maintenance;

internal sealed record SortKeySchema(string Kind, string Source, string IdColumn, string UpdatedColumn,
    string[] TextColumns, string[] KeyNames)
{
    public string Table => $"local_{Kind}_sort_keys";
    public string RevisionTable => $"local_{Kind}_sort_key_revision";
    public string Flag => $"local_{Kind}_sort_keys.synced_revision";
    public static SortKeySchema For(string? kind) => kind switch
    {
        "track" => new("track", "tracks", "track_id", "track_updated_at", ["title", "artist", "album"], ["title", "artist", "album"]),
        "album" => new("album", "albums", "album_id", "album_updated_at", ["title", "album_artist"], ["title", "artist"]),
        "artist" => new("artist", "artists", "artist_id", "artist_updated_at", ["name"], ["name"]),
        "playlist" => new("playlist", "playlist_sort_source", "item_id", "item_updated_at", ["title", "artist", "album"], ["title", "artist", "album"]),
        _ => throw new InvalidOperationException("Unsupported sort-key maintenance kind."),
    };
}
