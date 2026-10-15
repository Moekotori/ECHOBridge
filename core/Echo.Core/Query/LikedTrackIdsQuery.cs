using Echo.Core.Database;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class LikedTrackIdsQuery
{
    public const string Capability = "favorites.trackIds/v1";
    public const string AlbumCapability = "favorites.albumIds/v1";

    public static Dictionary<string, bool> Execute(ReadOnlyLibraryDatabase database, List<string>? ids, bool albums = false)
    {
        if (ids is { Count: > 256 }) throw new ArgumentException("At most 256 favorite IDs per request.");
        var result = (ids ?? []).Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal).ToDictionary(id => id, _ => false, StringComparer.Ordinal);
        if (result.Count == 0) return result;
        var placeholders = string.Join(',', result.Keys.Select(_ => "?"));
        // A status query never creates a playlist, repairs it, or counts its entire contents.
        using var reader = CachedQuery.Reader(database, $"""
            SELECT DISTINCT items.media_id FROM playlist_items AS items
            INNER JOIN playlists ON playlists.id = items.playlist_id
            WHERE playlists.kind = 'system' AND playlists.source_provider = 'local'
              AND playlists.source_playlist_id = '{(albums ? "liked-albums" : "liked-tracks")}'
              AND items.media_type IN ({(albums ? "'album'" : "'track', 'stream_track'")})
              AND items.media_id IN ({placeholders})
            """, command => { foreach (var id in result.Keys) command.Add(id); });
        while (reader.Read()) result[reader.GetString(0)] = true;
        return result;
    }
}
