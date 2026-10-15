using System.Globalization;
using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class PlaybackStatsDashboardQuery
{
    private const int ActivityDays = 371;

    public static PlaybackStatsDashboardDto Execute(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search)
    {
        if (!QueryCapabilities.SupportsPlaybackStatsDashboard(query))
        {
            throw new UnsupportedQueryException("getPlaybackStatsDashboard combination is not migrated.");
        }

        using var transaction = database.BeginDeferredRead();
        return UsesHistoryStats(query)
            ? ReadFromStats(database, query, includeTops: true)
            : ReadFiltered(database, query, search, includeTops: true);
    }

    public static PlaybackStatsDashboardDto ExecuteActivity(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search)
    {
        if (!QueryCapabilities.SupportsPlaybackStatsDashboardActivity(query))
        {
            throw new UnsupportedQueryException("getPlaybackStatsDashboardActivity combination is not migrated.");
        }

        using var transaction = database.BeginDeferredRead();
        return UsesHistoryStats(query) && query?.ActiveRemoteSourceIds is null
            ? ReadFromStats(database, query, includeTops: false)
            : ReadFiltered(database, query, search, includeTops: false);
    }

    private static bool UsesHistoryStats(CorePlaybackHistoryQuery? query) =>
        string.IsNullOrWhiteSpace(query?.Search)
        && string.IsNullOrWhiteSpace(query?.From)
        && string.IsNullOrWhiteSpace(query?.To)
        && query?.CompletedOnly != true
        && query?.MediaType is not ("local" or "streaming");

    private static PlaybackStatsDashboardDto ReadFromStats(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        bool includeTops)
    {
        var source = PlaybackHistoryFilter.Build(
            query,
            null,
            "history",
            PlaybackHistoryFilterKind.Stats,
            includeRange: false,
            includeSearch: false,
            includeMedia: false);
        var visible = string.IsNullOrWhiteSpace(source.ClauseSql)
            ? "playback_history_stats"
            : "visible_history";
        var visibleCte = string.IsNullOrWhiteSpace(source.ClauseSql)
            ? string.Empty
            : $"visible_history AS (SELECT * FROM playback_history_stats AS history {source.WhereSql})";
        string WithPrefix(string sql)
        {
            if (string.IsNullOrWhiteSpace(visibleCte))
            {
                return sql;
            }

            return sql.TrimStart().StartsWith("WITH ", StringComparison.Ordinal)
                ? sql.Replace("WITH ", $"WITH {visibleCte}, ", StringComparison.Ordinal)
                : $"WITH {visibleCte} {sql}";
        }

        var activityStart = UtcIso(DateTime.Now.Date.AddDays(-(ActivityDays - 1)));
        return new PlaybackStatsDashboardDto
        {
            GeneratedAt = GeneratedAt(),
            Totals = ReadTotals(database, WithPrefix($"""
                SELECT
                  COALESCE(SUM(play_count), 0),
                  COALESCE(SUM(completed_count), 0),
                  COALESCE(SUM(total_played_seconds), 0),
                  COUNT(*),
                  COUNT(DISTINCT COALESCE(NULLIF(TRIM(COALESCE(artist_snapshot, artist, '')), ''), 'Unknown Artist'))
                FROM {visible}
                """), source),
            TopTracks = includeTops ? ReadTopTracksFromStats(database, WithPrefix, visible, source) : [],
            TopArtists = includeTops ? ReadBreakdownArtists(database, WithPrefix($"""
                SELECT
                  COALESCE(NULLIF(TRIM(COALESCE(artist_snapshot, artist, '')), ''), 'Unknown Artist'),
                  COALESCE(SUM(play_count), 0),
                  COALESCE(SUM(completed_count), 0),
                  COALESCE(SUM(total_played_seconds), 0)
                FROM {visible}
                GROUP BY 1
                ORDER BY 2 DESC, 4 DESC, 1 COLLATE NOCASE
                LIMIT 8
                """), source) : [],
            TopAlbums = includeTops ? ReadTopAlbumsFromStats(database, WithPrefix, visible, source) : [],
            FormatBreakdown = includeTops ? ReadBreakdown(database, WithPrefix($"""
                SELECT
                  CASE
                    WHEN history.media_type = 'streaming' THEN 'Streaming' || CASE WHEN history.provider IS NOT NULL THEN char(32) || char(183) || char(32) || history.provider ELSE '' END
                    WHEN history.media_type = 'remote' THEN 'Remote'
                    WHEN NULLIF(TRIM(COALESCE(tracks.codec, '')), '') IS NOT NULL THEN UPPER(tracks.codec)
                    WHEN history.media_type = 'local' THEN 'Local'
                    ELSE 'Unknown'
                  END AS label,
                  COALESCE(SUM(history.play_count), 0) AS play_count,
                  COALESCE(SUM(history.total_played_seconds), 0) AS played_seconds
                FROM {visible} AS history
                LEFT JOIN tracks ON tracks.id = history.track_id AND tracks.missing = 0
                GROUP BY 1
                ORDER BY play_count DESC, played_seconds DESC, label COLLATE NOCASE
                LIMIT 8
                """), source) : [],
            QualityBreakdown = includeTops ? ReadBreakdown(database, WithPrefix($"""
                SELECT
                  CASE
                    WHEN history.media_type = 'streaming' THEN 'Streaming'
                    WHEN history.media_type = 'remote' THEN 'Remote'
                    WHEN (tracks.sample_rate >= 88200 AND tracks.bit_depth >= 24) OR UPPER(COALESCE(tracks.codec, '')) IN ('DSF', 'DFF', 'DSD') THEN 'Hi-Res'
                    WHEN LOWER(COALESCE(tracks.codec, '')) IN ('flac', 'wav', 'wave', 'alac', 'aiff', 'aif', 'ape', 'wv', 'tta', 'dsf', 'dff') THEN 'Lossless'
                    WHEN tracks.bitrate >= 320000 THEN 'High Bitrate'
                    WHEN tracks.codec IS NOT NULL OR tracks.bitrate IS NOT NULL THEN 'Lossy'
                    ELSE 'Unknown'
                  END AS label,
                  COALESCE(SUM(history.play_count), 0) AS play_count,
                  COALESCE(SUM(history.total_played_seconds), 0) AS played_seconds
                FROM {visible} AS history
                LEFT JOIN tracks ON tracks.id = history.track_id AND tracks.missing = 0
                GROUP BY 1
                ORDER BY play_count DESC, played_seconds DESC, label COLLATE NOCASE
                LIMIT 8
                """), source) : [],
            DailyActivity = ReadDays(database, query, null, activityStart),
        };
    }

    private static PlaybackStatsDashboardDto ReadFiltered(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search,
        bool includeTops)
    {
        var filter = PlaybackHistoryFilter.Build(query, search, "history", PlaybackHistoryFilterKind.Events);
        var activityStart = UtcIso(DateTime.Now.Date.AddDays(-(ActivityDays - 1)));
        const string historyKeySql = "COALESCE(history.stable_key, history.track_id, history.track_path)";
        return new PlaybackStatsDashboardDto
        {
            GeneratedAt = GeneratedAt(),
            Totals = ReadTotals(database, $"""
                SELECT
                  COUNT(*),
                  COALESCE(SUM(CASE WHEN history.completed > 0 THEN 1 ELSE 0 END), 0),
                  COALESCE(SUM(history.played_seconds), 0),
                  COUNT(DISTINCT {historyKeySql}),
                  COUNT(DISTINCT COALESCE(NULLIF(TRIM(COALESCE(history.artist_snapshot, history.artist, '')), ''), 'Unknown Artist'))
                FROM playback_history AS history
                {filter.WhereSql}
                """, filter),
            TopTracks = includeTops ? ReadTopTracksFiltered(database, filter, historyKeySql) : [],
            TopArtists = includeTops ? ReadBreakdownArtists(database, $"""
                SELECT
                  COALESCE(NULLIF(TRIM(COALESCE(history.artist_snapshot, history.artist, '')), ''), 'Unknown Artist'),
                  COUNT(*),
                  COALESCE(SUM(CASE WHEN history.completed > 0 THEN 1 ELSE 0 END), 0),
                  COALESCE(SUM(history.played_seconds), 0)
                FROM playback_history AS history
                {filter.WhereSql}
                GROUP BY 1
                ORDER BY 2 DESC, 4 DESC, 1 COLLATE NOCASE
                LIMIT 8
                """, filter) : [],
            TopAlbums = includeTops ? ReadTopAlbumsFiltered(database, filter, historyKeySql) : [],
            FormatBreakdown = includeTops ? ReadBreakdown(database, $"""
                SELECT
                  CASE
                    WHEN history.media_type = 'streaming' THEN 'Streaming' || CASE WHEN history.provider IS NOT NULL THEN ' · ' || history.provider ELSE '' END
                    WHEN history.media_type = 'remote' THEN 'Remote'
                    WHEN NULLIF(TRIM(COALESCE(tracks.codec, '')), '') IS NOT NULL THEN UPPER(tracks.codec)
                    WHEN history.media_type = 'local' THEN 'Local'
                    ELSE 'Unknown'
                  END AS label,
                  COUNT(*) AS play_count,
                  COALESCE(SUM(history.played_seconds), 0) AS played_seconds
                FROM playback_history AS history
                LEFT JOIN tracks ON tracks.id = history.track_id AND tracks.missing = 0
                {filter.WhereSql}
                GROUP BY 1
                ORDER BY play_count DESC, played_seconds DESC, label COLLATE NOCASE
                LIMIT 8
                """, filter) : [],
            QualityBreakdown = includeTops ? ReadBreakdown(database, $"""
                SELECT
                  CASE
                    WHEN history.media_type = 'streaming' THEN 'Streaming'
                    WHEN history.media_type = 'remote' THEN 'Remote'
                    WHEN (tracks.sample_rate >= 88200 AND tracks.bit_depth >= 24) OR UPPER(COALESCE(tracks.codec, '')) IN ('DSF', 'DFF', 'DSD') THEN 'Hi-Res'
                    WHEN LOWER(COALESCE(tracks.codec, '')) IN ('flac', 'wav', 'wave', 'alac', 'aiff', 'aif', 'ape', 'wv', 'tta', 'dsf', 'dff') THEN 'Lossless'
                    WHEN tracks.bitrate >= 320000 THEN 'High Bitrate'
                    WHEN tracks.codec IS NOT NULL OR tracks.bitrate IS NOT NULL THEN 'Lossy'
                    ELSE 'Unknown'
                  END AS label,
                  COUNT(*) AS play_count,
                  COALESCE(SUM(history.played_seconds), 0) AS played_seconds
                FROM playback_history AS history
                LEFT JOIN tracks ON tracks.id = history.track_id AND tracks.missing = 0
                {filter.WhereSql}
                GROUP BY 1
                ORDER BY play_count DESC, played_seconds DESC, label COLLATE NOCASE
                LIMIT 8
                """, filter) : [],
            DailyActivity = ReadDays(database, query, search, activityStart),
        };
    }

    private static PlaybackStatsTotalsDto ReadTotals(
        ReadOnlyLibraryDatabase database,
        string sql,
        PlaybackHistoryFilter filter)
    {
        using var reader = CachedQuery.Reader(database, sql, filter.Bind);
        if (!reader.Read())
        {
            return new PlaybackStatsTotalsDto();
        }

        return new PlaybackStatsTotalsDto
        {
            PlayCount = Int(reader, 0),
            CompletedCount = Int(reader, 1),
            PlayedSeconds = Real(reader, 2),
            UniqueTracks = Int(reader, 3),
            UniqueArtists = Int(reader, 4),
        };
    }

    private static List<PlaybackStatsTrackDto> ReadTopTracksFromStats(
        ReadOnlyLibraryDatabase database,
        Func<string, string> withPrefix,
        string visible,
        PlaybackHistoryFilter filter)
    {
        var items = new List<PlaybackStatsTrackDto>();
        using var reader = CachedQuery.Reader(database, withPrefix($"""
            SELECT
              history_key,
              track_id,
              title_snapshot,
              artist_snapshot,
              album_snapshot,
              title,
              artist,
              album,
              cover_id,
              cover_snapshot,
              duration_seconds,
              play_count,
              completed_count,
              total_played_seconds,
              last_started_at
            FROM {visible}
            ORDER BY play_count DESC, total_played_seconds DESC, last_started_at DESC
            LIMIT 8
            """), filter.Bind);
        while (reader.Read())
        {
            items.Add(MapTrack(reader));
        }

        return items;
    }

    private static List<PlaybackStatsTrackDto> ReadTopTracksFiltered(
        ReadOnlyLibraryDatabase database,
        PlaybackHistoryFilter filter,
        string historyKeySql)
    {
        var items = new List<PlaybackStatsTrackDto>();
        using var reader = CachedQuery.Reader(database, $"""
            WITH filtered_history AS (
              SELECT history.*, {historyKeySql} AS history_key
              FROM playback_history AS history
              {filter.WhereSql}
            ),
            ranked_history AS (
              SELECT
                filtered_history.*,
                COUNT(*) OVER (PARTITION BY history_key) AS play_count,
                COALESCE(SUM(CASE WHEN completed > 0 THEN 1 ELSE 0 END) OVER (PARTITION BY history_key), 0) AS completed_count,
                COALESCE(SUM(played_seconds) OVER (PARTITION BY history_key), 0) AS group_played_seconds,
                MAX(started_at) OVER (PARTITION BY history_key) AS last_played_at,
                ROW_NUMBER() OVER (
                  PARTITION BY history_key
                  ORDER BY started_at DESC, created_at DESC, id DESC
                ) AS group_rank
              FROM filtered_history
            )
            SELECT
              history_key,
              track_id,
              title_snapshot,
              artist_snapshot,
              album_snapshot,
              title,
              artist,
              album,
              cover_id,
              cover_snapshot,
              duration_seconds,
              play_count,
              completed_count,
              group_played_seconds,
              last_played_at
            FROM ranked_history
            WHERE group_rank = 1
            ORDER BY play_count DESC, group_played_seconds DESC, last_played_at DESC
            LIMIT 8
            """, filter.Bind);
        while (reader.Read())
        {
            items.Add(MapTrack(reader));
        }

        return items;
    }

    private static PlaybackStatsTrackDto MapTrack(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var coverId = Text(reader, 8);
        return new PlaybackStatsTrackDto
        {
            Id = Text(reader, 0) ?? string.Empty,
            TrackId = Text(reader, 1),
            Title = Text(reader, 2) ?? Text(reader, 5) ?? string.Empty,
            Artist = Text(reader, 3) ?? Text(reader, 6) ?? string.Empty,
            Album = Text(reader, 4) ?? Text(reader, 7) ?? string.Empty,
            CoverThumb = !string.IsNullOrEmpty(coverId)
                ? LibraryText.CoverUrl(coverId, "thumb")
                : Text(reader, 9),
            DurationSeconds = Real(reader, 10),
            PlayCount = Int(reader, 11),
            CompletedCount = Int(reader, 12),
            PlayedSeconds = Real(reader, 13),
            LastPlayedAt = Text(reader, 14),
        };
    }

    private static List<PlaybackStatsArtistDto> ReadBreakdownArtists(
        ReadOnlyLibraryDatabase database,
        string sql,
        PlaybackHistoryFilter filter)
    {
        var items = new List<PlaybackStatsArtistDto>();
        using var reader = CachedQuery.Reader(database, sql, filter.Bind);
        while (reader.Read())
        {
            items.Add(new PlaybackStatsArtistDto
            {
                Artist = Text(reader, 0) ?? LibraryText.UnknownArtist,
                PlayCount = Int(reader, 1),
                CompletedCount = Int(reader, 2),
                PlayedSeconds = Real(reader, 3),
            });
        }

        return items;
    }

    private static List<PlaybackStatsAlbumDto> ReadTopAlbumsFromStats(
        ReadOnlyLibraryDatabase database,
        Func<string, string> withPrefix,
        string visible,
        PlaybackHistoryFilter filter) =>
        ReadAlbums(database, withPrefix($"""
            WITH album_history AS (
              SELECT
                history.*,
                (
                  SELECT album_tracks.album_id
                  FROM album_tracks
                  INNER JOIN tracks ON tracks.id = album_tracks.track_id AND tracks.missing = 0
                  WHERE album_tracks.track_id = history.track_id
                  ORDER BY album_tracks.position ASC
                  LIMIT 1
                ) AS resolved_album_id
              FROM {visible} AS history
            ),
            album_rows AS (
              SELECT
                album_history.*,
                albums.id AS album_id,
                albums.album_key,
                albums.title AS album_title,
                albums.album_artist AS album_artist_name,
                albums.year AS album_year,
                albums.track_count AS album_track_count,
                albums.duration AS album_duration,
                albums.cover_id AS album_cover_id,
                COALESCE(
                  albums.id,
                  'history:' ||
                    lower(trim(COALESCE(NULLIF(TRIM(album_history.album_artist), ''), NULLIF(TRIM(album_history.artist_snapshot), ''), NULLIF(TRIM(album_history.artist), ''), 'Unknown Artist'))) ||
                    char(31) ||
                    lower(trim(COALESCE(NULLIF(TRIM(album_history.album_snapshot), ''), NULLIF(TRIM(album_history.album), ''), 'Unknown Album')))
                ) AS album_group_key
              FROM album_history
              LEFT JOIN albums ON albums.id = album_history.resolved_album_id
              WHERE NULLIF(TRIM(COALESCE(albums.title, album_history.album_snapshot, album_history.album, '')), '') IS NOT NULL
            ),
            ranked_album_history AS (
              SELECT
                album_rows.*,
                COALESCE(SUM(play_count) OVER (PARTITION BY album_group_key), 0) AS group_play_count,
                COALESCE(SUM(completed_count) OVER (PARTITION BY album_group_key), 0) AS group_completed_count,
                COALESCE(SUM(total_played_seconds) OVER (PARTITION BY album_group_key), 0) AS group_played_seconds,
                MAX(last_started_at) OVER (PARTITION BY album_group_key) AS group_last_played_at,
                ROW_NUMBER() OVER (
                  PARTITION BY album_group_key
                  ORDER BY last_started_at DESC, updated_at DESC, history_key DESC
                ) AS group_rank
              FROM album_rows
            )
            SELECT
              album_group_key,
              album_id,
              album_key,
              media_type,
              COALESCE(album_title, album_snapshot, album, 'Unknown Album'),
              COALESCE(album_artist_name, album_artist, artist_snapshot, artist, 'Unknown Artist'),
              album_year,
              COALESCE(album_track_count, 1),
              COALESCE(album_duration, duration_seconds, duration_snapshot, 0),
              COALESCE(album_cover_id, cover_id),
              cover_snapshot,
              group_play_count,
              group_completed_count,
              group_played_seconds,
              group_last_played_at
            FROM ranked_album_history
            WHERE group_rank = 1
            ORDER BY group_play_count DESC, group_played_seconds DESC, group_last_played_at DESC
            LIMIT 8
            """), filter);

    private static List<PlaybackStatsAlbumDto> ReadTopAlbumsFiltered(
        ReadOnlyLibraryDatabase database,
        PlaybackHistoryFilter filter,
        string historyKeySql) =>
        ReadAlbums(database, $"""
            WITH filtered_history AS (
              SELECT history.*, {historyKeySql} AS history_key
              FROM playback_history AS history
              {filter.WhereSql}
            ),
            album_history AS (
              SELECT
                filtered_history.*,
                (
                  SELECT album_tracks.album_id
                  FROM album_tracks
                  INNER JOIN tracks ON tracks.id = album_tracks.track_id AND tracks.missing = 0
                  WHERE album_tracks.track_id = filtered_history.track_id
                  ORDER BY album_tracks.position ASC
                  LIMIT 1
                ) AS resolved_album_id
              FROM filtered_history
            ),
            album_rows AS (
              SELECT
                album_history.*,
                albums.id AS album_id,
                albums.album_key,
                albums.title AS album_title,
                albums.album_artist AS album_artist_name,
                albums.year AS album_year,
                albums.track_count AS album_track_count,
                albums.duration AS album_duration,
                albums.cover_id AS album_cover_id,
                COALESCE(
                  albums.id,
                  'history:' ||
                    lower(trim(COALESCE(NULLIF(TRIM(album_history.album_artist), ''), NULLIF(TRIM(album_history.artist_snapshot), ''), NULLIF(TRIM(album_history.artist), ''), 'Unknown Artist'))) ||
                    char(31) ||
                    lower(trim(COALESCE(NULLIF(TRIM(album_history.album_snapshot), ''), NULLIF(TRIM(album_history.album), ''), 'Unknown Album')))
                ) AS album_group_key
              FROM album_history
              LEFT JOIN albums ON albums.id = album_history.resolved_album_id
              WHERE NULLIF(TRIM(COALESCE(albums.title, album_history.album_snapshot, album_history.album, '')), '') IS NOT NULL
            ),
            ranked_album_history AS (
              SELECT
                album_rows.*,
                COUNT(*) OVER (PARTITION BY album_group_key) AS group_play_count,
                COALESCE(SUM(CASE WHEN completed > 0 THEN 1 ELSE 0 END) OVER (PARTITION BY album_group_key), 0) AS group_completed_count,
                COALESCE(SUM(played_seconds) OVER (PARTITION BY album_group_key), 0) AS group_played_seconds,
                MAX(started_at) OVER (PARTITION BY album_group_key) AS group_last_played_at,
                ROW_NUMBER() OVER (
                  PARTITION BY album_group_key
                  ORDER BY started_at DESC, created_at DESC, id DESC
                ) AS group_rank
              FROM album_rows
            )
            SELECT
              album_group_key,
              album_id,
              album_key,
              media_type,
              COALESCE(album_title, album_snapshot, album, 'Unknown Album'),
              COALESCE(album_artist_name, album_artist, artist_snapshot, artist, 'Unknown Artist'),
              album_year,
              COALESCE(album_track_count, 1),
              COALESCE(album_duration, duration_seconds, duration_snapshot, 0),
              COALESCE(album_cover_id, cover_id),
              cover_snapshot,
              group_play_count,
              group_completed_count,
              group_played_seconds,
              group_last_played_at
            FROM ranked_album_history
            WHERE group_rank = 1
            ORDER BY group_play_count DESC, group_played_seconds DESC, group_last_played_at DESC
            LIMIT 8
            """, filter);

    private static List<PlaybackStatsAlbumDto> ReadAlbums(
        ReadOnlyLibraryDatabase database,
        string sql,
        PlaybackHistoryFilter filter)
    {
        var items = new List<PlaybackStatsAlbumDto>();
        using var reader = CachedQuery.Reader(database, sql, filter.Bind);
        while (reader.Read())
        {
            var coverId = Text(reader, 9);
            var mediaType = Text(reader, 3);
            items.Add(new PlaybackStatsAlbumDto
            {
                Id = Text(reader, 0) ?? string.Empty,
                AlbumId = Text(reader, 1),
                MediaType = mediaType == "remote" ? "remote" : "local",
                AlbumKey = Text(reader, 2),
                Title = Text(reader, 4) ?? "Unknown Album",
                AlbumArtist = Text(reader, 5) ?? LibraryText.UnknownArtist,
                Year = reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6)),
                TrackCount = Int(reader, 7),
                Duration = Real(reader, 8),
                CoverId = coverId,
                CoverThumb = !string.IsNullOrEmpty(coverId)
                    ? LibraryText.CoverUrl(coverId, "album")
                    : Text(reader, 10),
                PlayCount = Int(reader, 11),
                CompletedCount = Int(reader, 12),
                PlayedSeconds = Real(reader, 13),
                LastPlayedAt = Text(reader, 14),
            });
        }

        return items;
    }

    private static List<PlaybackStatsBreakdownDto> ReadBreakdown(
        ReadOnlyLibraryDatabase database,
        string sql,
        PlaybackHistoryFilter filter)
    {
        var items = new List<PlaybackStatsBreakdownDto>();
        using var reader = CachedQuery.Reader(database, sql, filter.Bind);
        while (reader.Read())
        {
            var label = Text(reader, 0) ?? "Unknown";
            items.Add(new PlaybackStatsBreakdownDto
            {
                Id = label,
                Label = label,
                PlayCount = Int(reader, 1),
                PlayedSeconds = Real(reader, 2),
            });
        }

        return items;
    }

    private static List<PlaybackStatsDayDto> ReadDays(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search,
        string activityStart)
    {
        var filter = PlaybackHistoryFilter.Build(query, search, "history", PlaybackHistoryFilterKind.Events);
        if (string.IsNullOrWhiteSpace(query?.From) && string.IsNullOrWhiteSpace(query?.To))
        {
            filter = filter.Append("history.started_at >= ?", activityStart);
        }

        var items = new List<PlaybackStatsDayDto>();
        using var reader = CachedQuery.Reader(database, $"""
            SELECT
              substr(history.started_at, 1, 10),
              COUNT(*),
              COALESCE(SUM(history.played_seconds), 0)
            FROM playback_history AS history
            {filter.WhereSql}
            GROUP BY 1
            ORDER BY 1 DESC
            LIMIT 371
            """, filter.Bind);
        while (reader.Read())
        {
            items.Add(new PlaybackStatsDayDto
            {
                Date = Text(reader, 0) ?? string.Empty,
                PlayCount = Int(reader, 1),
                PlayedSeconds = Real(reader, 2),
            });
        }

        items.Reverse();
        return items;
    }

    private static string GeneratedAt() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string UtcIso(DateTime local) =>
        local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string? Text(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static int Int(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static double Real(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToDouble(reader.GetValue(ordinal));
}
