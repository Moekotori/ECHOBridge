using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class PlaylistListQuery
{
    private static readonly HashSet<string> PlaylistSortModes = new(StringComparer.Ordinal)
    {
        "manual",
        "titleAsc",
        "titleDesc",
        "artistAsc",
        "artistDesc",
        "album",
        "albumDesc",
        "artistAlbum",
        "addedDesc",
        "addedAsc",
        "durationAsc",
        "durationDesc",
    };

    public static List<LibraryPlaylistDto> Execute(ReadOnlyLibraryDatabase database)
    {
        const string sql = """
            SELECT
              id,
              name,
              description,
              kind,
              source_provider,
              source_playlist_id,
              cover_id,
              cover_url,
              sort_mode,
              item_count,
              created_at,
              updated_at
            FROM playlists
            WHERE NOT (kind = 'system' AND source_provider = 'local' AND source_playlist_id IN ('liked-tracks', 'liked-albums'))
            ORDER BY updated_at DESC, name COLLATE NOCASE
            """;

        using var transaction = database.BeginDeferredRead();
        var items = new List<LibraryPlaylistDto>();
        using var reader = CachedQuery.Reader(database, sql);
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        reader.Close();
        foreach (var item in items)
        {
            Echo.Core.SmartPlaylists.SmartPlaylistQueries.Enrich(database,item);
            PlaylistCoverQuery.Enrich(database,item);
        }
        return items;
    }


    internal static LibraryPlaylistDto Map(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
            var coverId = Text(reader, 6);
            var coverUrl = Text(reader, 7);
            var kind = Text(reader, 3);
            return new LibraryPlaylistDto
            {
                Id = Text(reader, 0) ?? string.Empty,
                Name = Text(reader, 1) ?? string.Empty,
                Description = Text(reader, 2),
                Kind = kind is "smart" or "synced" or "system" ? kind : "manual",
                SourceProvider = MapSourceProvider(Text(reader, 4)),
                SourcePlaylistId = Text(reader, 5),
                CoverId = coverId,
                CoverThumb = !string.IsNullOrEmpty(coverId)
                    ? LibraryText.CoverUrl(coverId, "album")
                    : coverUrl,
                SortMode = PlaylistSortModes.Contains(Text(reader, 8) ?? string.Empty)
                    ? Text(reader, 8)!
                    : "manual",
                ItemCount = reader.IsDBNull(9) ? 0 : Convert.ToInt32(reader.GetValue(9)),
                CreatedAt = Text(reader, 10) ?? string.Empty,
                UpdatedAt = Text(reader, 11) ?? string.Empty,
            };
    }

    private static string MapSourceProvider(string? value) =>
        value is "netease" or "qqmusic" or "kugou" or "spotify" or "remote" ? value : "local";

    private static string? Text(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
