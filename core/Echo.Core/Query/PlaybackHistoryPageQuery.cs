using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class PlaybackHistoryPageQuery
{
    private const int DefaultHistoryPageSize = 50;

    public static LibraryPageDto<PlaybackHistoryEntryDto> Execute(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search)
    {
        if (!QueryCapabilities.SupportsPlaybackHistory(query))
        {
            throw new UnsupportedQueryException("getPlaybackHistory combination is not migrated.");
        }

        using var transaction = database.BeginDeferredRead();
        if (string.Equals(query?.View, "records", StringComparison.Ordinal))
        {
            return PlaybackHistoryRecordsQuery.Execute(database, query, search);
        }

        return PlaybackHistoryFilter.HasTimeRange(query)
            ? ExecuteWindowed(database, query, search)
            : ExecuteStats(database, query, search);
    }

    private static LibraryPageDto<PlaybackHistoryEntryDto> ExecuteStats(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search)
    {
        var (page, pageSize, offset) = NormalizePage(query);
        var filter = PlaybackHistoryFilter.Build(query, search, "playback_history_stats", PlaybackHistoryFilterKind.Stats);
        var orderSql = "ORDER BY " + PlaybackHistoryOrder.Build(query, "stats");

        var total = Convert.ToInt64(CachedQuery.Scalar(database, $"""
            SELECT COUNT(*) FROM playback_history_stats
            {filter.WhereSql}
            """, filter.Bind) ?? 0);
        var items = new List<PlaybackHistoryEntryDto>();
        using var reader = CachedQuery.Reader(database, $"""
            SELECT
              history_key AS id,
              track_id,
              track_path,
              media_type,
              provider,
              provider_track_id,
              stable_key,
              title_snapshot,
              artist_snapshot,
              album_snapshot,
              duration_snapshot,
              cover_snapshot,
              title,
              artist,
              album,
              album_artist,
              cover_id,
              last_started_at AS started_at,
              last_ended_at AS ended_at,
              total_played_seconds AS played_seconds,
              duration_seconds,
              play_count AS history_play_count,
              completed_count,
              source_type,
              source_label,
              queue_id
            FROM playback_history_stats
            {filter.WhereSql}
            {orderSql}
            LIMIT ? OFFSET ?
            """, command =>
        {
            filter.Bind(command);
            command.Add(pageSize);
            command.Add(offset);
        });
        while (reader.Read())
        {
            items.Add(MapStatsEntry(reader));
        }

        return Page(items, page, pageSize, offset, total);
    }

    private static LibraryPageDto<PlaybackHistoryEntryDto> ExecuteWindowed(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search)
    {
        var (page, pageSize, offset) = NormalizePage(query);
        var filter = PlaybackHistoryFilter.Build(query, search, "playback_history", PlaybackHistoryFilterKind.Events);
        var orderSql = PlaybackHistoryOrder.Build(query, "window");

        var total = Convert.ToInt64(CachedQuery.Scalar(database, $"""
            SELECT COUNT(DISTINCT COALESCE(stable_key, track_id, track_path))
            FROM playback_history
            {filter.WhereSql}
            """, filter.Bind) ?? 0);
        var items = new List<PlaybackHistoryEntryDto>();
        using var reader = CachedQuery.Reader(database, $"""
            WITH filtered_history AS (
              SELECT playback_history.*, COALESCE(stable_key, track_id, track_path) AS history_key
              FROM playback_history
              {filter.WhereSql}
            ),
            ranked_history AS (
              SELECT
                filtered_history.*,
                COUNT(*) OVER (PARTITION BY history_key) AS history_play_count,
                COALESCE(SUM(played_seconds) OVER (PARTITION BY history_key), 0) AS history_played_seconds_total,
                COALESCE(SUM(CASE WHEN completed > 0 THEN 1 ELSE 0 END) OVER (PARTITION BY history_key), 0) AS completed_count,
                MAX(started_at) OVER (PARTITION BY history_key) AS last_started_at,
                MAX(ended_at) OVER (PARTITION BY history_key) AS last_ended_at,
                ROW_NUMBER() OVER (
                  PARTITION BY history_key
                  ORDER BY started_at DESC, created_at DESC, id DESC
                ) AS group_rank
              FROM filtered_history
            )
            SELECT
              history_key AS id,
              track_id,
              track_path,
              media_type,
              provider,
              provider_track_id,
              stable_key,
              title_snapshot,
              artist_snapshot,
              album_snapshot,
              duration_snapshot,
              cover_snapshot,
              title,
              artist,
              album,
              album_artist,
              cover_id,
              last_started_at AS started_at,
              last_ended_at AS ended_at,
              history_played_seconds_total,
              duration_seconds,
              history_play_count,
              completed_count,
              source_type,
              source_label,
              queue_id
            FROM ranked_history
            WHERE group_rank = 1
            ORDER BY {orderSql}
            LIMIT ? OFFSET ?
            """, command =>
        {
            filter.Bind(command);
            command.Add(pageSize);
            command.Add(offset);
        });
        while (reader.Read())
        {
            items.Add(MapStatsEntry(reader));
        }

        return Page(items, page, pageSize, offset, total);
    }

    private static (int Page, int PageSize, int Offset) NormalizePage(CorePlaybackHistoryQuery? query)
    {
        var page = Math.Max(1, query?.Page ?? 1);
        var pageSize = Math.Clamp(query?.PageSize ?? DefaultHistoryPageSize, 1, ProtocolConstants.MaxPageSize);
        if (page > 1 && page - 1 > int.MaxValue / pageSize)
        {
            throw new UnsupportedQueryException("getPlaybackHistory page offset overflows.");
        }

        return (page, pageSize, (page - 1) * pageSize);
    }

    private static LibraryPageDto<PlaybackHistoryEntryDto> Page(
        List<PlaybackHistoryEntryDto> items,
        int page,
        int pageSize,
        int offset,
        long total) =>
        new()
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total,
            HasMore = offset + items.Count < total,
        };

    private static PlaybackHistoryEntryDto MapStatsEntry(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var coverId = Text(reader, 16);
        var mediaType = Text(reader, 3);
        var titleSnapshot = Text(reader, 7);
        var artistSnapshot = Text(reader, 8);
        var albumSnapshot = Text(reader, 9);
        return new PlaybackHistoryEntryDto
        {
            Id = Text(reader, 0) ?? string.Empty,
            TrackId = Text(reader, 1),
            TrackPath = Text(reader, 2) ?? string.Empty,
            MediaType = mediaType is "remote" or "streaming" ? mediaType : "local",
            Provider = Text(reader, 4),
            ProviderTrackId = Text(reader, 5),
            StableKey = Text(reader, 6),
            Title = titleSnapshot ?? Text(reader, 12) ?? string.Empty,
            Artist = artistSnapshot ?? Text(reader, 13) ?? string.Empty,
            Album = albumSnapshot ?? Text(reader, 14) ?? string.Empty,
            AlbumArtist = Text(reader, 15) ?? string.Empty,
            CoverId = coverId,
            CoverThumb = !string.IsNullOrEmpty(coverId)
                ? LibraryText.CoverUrl(coverId, "thumb")
                : Text(reader, 11),
            StartedAt = Text(reader, 17) ?? string.Empty,
            EndedAt = Text(reader, 18),
            PlayedSeconds = Real(reader, 19),
            DurationSeconds = Real(reader, 20),
            DurationSnapshot = reader.IsDBNull(10) ? null : Convert.ToDouble(reader.GetValue(10)),
            CoverSnapshot = Text(reader, 11),
            PlayCount = Int(reader, 21),
            Completed = Int(reader, 22) > 0,
            SourceType = Text(reader, 23),
            SourceLabel = Text(reader, 24),
            QueueId = Text(reader, 25),
        };
    }

    private static string? Text(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static int Int(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static double Real(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToDouble(reader.GetValue(ordinal));
}
