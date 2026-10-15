using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class AlbumFocusFacetsQuery
{
    private const int MaxGenreFacets = 16;

    private static readonly string[] AudioFormatOrder =
    [
        "sampleRate44100",
        "sampleRate48000",
        "sampleRate88200",
        "sampleRate96000",
        "sampleRate176400",
        "sampleRate192000",
        "sampleRate352800",
        "sampleRate384000",
        "dsd",
        "otherSampleRate",
    ];

    public static AlbumFocusFacetsDto Execute(ReadOnlyLibraryDatabase database)
    {
        using var transaction = database.BeginDeferredRead();
        var bitDepth24 = CountMatchingAlbums(database, "COALESCE(tracks.bit_depth, 0) >= 24");
        var flac = CountMatchingAlbums(
            database,
            "(UPPER(TRIM(COALESCE(tracks.codec, ''))) = 'FLAC' OR LOWER(COALESCE(tracks.path, '')) LIKE '%.flac')");
        return new AlbumFocusFacetsDto
        {
            Decades = ListDecades(database),
            Genres = ListGenres(database),
            AudioFormats = ListAudioFormats(database),
            BitDepths = bitDepth24 > 0
                ? [new AlbumFocusBitDepthFacetDto { Bits = 24, AlbumCount = bitDepth24 }]
                : [],
            FileFormats = flac > 0
                ? [new AlbumFocusFileFormatFacetDto { Id = "flac", AlbumCount = flac }]
                : [],
        };
    }

    private static List<AlbumFocusDecadeFacetDto> ListDecades(ReadOnlyLibraryDatabase database)
    {
        const string sql = """
            SELECT
              CAST((year / 10) * 10 AS INTEGER) AS decade,
              COUNT(*) AS album_count
            FROM albums
            WHERE year IS NOT NULL
              AND year BETWEEN 1000 AND 2100
            GROUP BY decade
            ORDER BY decade DESC
            """;
        var items = new List<AlbumFocusDecadeFacetDto>();
        using var reader = CachedQuery.Reader(database, sql);
        while (reader.Read())
        {
            var decade = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var albumCount = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            if (decade >= 1000 && albumCount > 0)
            {
                items.Add(new AlbumFocusDecadeFacetDto { Decade = decade, AlbumCount = albumCount });
            }
        }

        return items;
    }

    private static List<AlbumFocusGenreFacetDto> ListGenres(ReadOnlyLibraryDatabase database)
    {
        const string sql = """
            SELECT
              echo_genre_key(tracks.genre) AS genre_key,
              MIN(TRIM(tracks.genre)) AS display_name,
              COUNT(DISTINCT albums.id) AS album_count
            FROM albums
            INNER JOIN album_tracks ON album_tracks.album_id = albums.id
            INNER JOIN tracks ON tracks.id = album_tracks.track_id
            WHERE tracks.missing = 0 AND tracks.library_hidden = 0
              AND echo_genre_key(tracks.genre) != ?
            GROUP BY echo_genre_key(tracks.genre)
            ORDER BY album_count DESC, display_name COLLATE NOCASE ASC
            LIMIT ?
            """;
        var items = new List<AlbumFocusGenreFacetDto>();
        using var reader = CachedQuery.Reader(database, sql, command =>
        {
            command.Add(GenreKey.Unclassified);
            command.Add(MaxGenreFacets);
        });
        while (reader.Read())
        {
            var genreKey = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var displayName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var albumCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            if (genreKey.Length > 0 && displayName.Length > 0 && albumCount > 0)
            {
                items.Add(new AlbumFocusGenreFacetDto
                {
                    GenreKey = genreKey,
                    DisplayName = displayName,
                    AlbumCount = albumCount,
                });
            }
        }

        return items;
    }

    private static List<AlbumFocusAudioFormatFacetDto> ListAudioFormats(ReadOnlyLibraryDatabase database)
    {
        var sql = $"""
            SELECT
              {AlbumFocusSql.AudioFormatCaseSql()} AS format_id,
              COUNT(DISTINCT albums.id) AS album_count
            FROM albums
            INNER JOIN album_tracks ON album_tracks.album_id = albums.id
            INNER JOIN tracks ON tracks.id = album_tracks.track_id
            WHERE tracks.missing = 0 AND tracks.library_hidden = 0
            GROUP BY format_id
            HAVING format_id IS NOT NULL
            ORDER BY album_count DESC
            """;
        var known = new HashSet<string>(AudioFormatOrder, StringComparer.Ordinal);
        var order = AudioFormatOrder
            .Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        var items = new List<AlbumFocusAudioFormatFacetDto>();
        using var reader = CachedQuery.Reader(database, sql);
        while (reader.Read())
        {
            var id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var albumCount = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            if (known.Contains(id) && albumCount > 0)
            {
                items.Add(new AlbumFocusAudioFormatFacetDto { Id = id, AlbumCount = albumCount });
            }
        }

        items.Sort((left, right) =>
        {
            var leftOrder = order.GetValueOrDefault(left.Id, 99);
            var rightOrder = order.GetValueOrDefault(right.Id, 99);
            return leftOrder.CompareTo(rightOrder);
        });
        return items;
    }

    private static int CountMatchingAlbums(ReadOnlyLibraryDatabase database, string predicateSql)
    {
        var sql = $"""
            SELECT COUNT(DISTINCT albums.id)
            FROM albums
            INNER JOIN album_tracks ON album_tracks.album_id = albums.id
            INNER JOIN tracks ON tracks.id = album_tracks.track_id
            WHERE tracks.missing = 0 AND tracks.library_hidden = 0
              AND ({predicateSql})
            """;
        return Convert.ToInt32(CachedQuery.Scalar(database, sql) ?? 0);
    }
}
