using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class PlaybackHistoryRecordsQuery
{
    private const int DefaultHistoryPageSize = 50;

    public static LibraryPageDto<PlaybackHistoryEntryDto> Execute(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search)
    {
        var page = Math.Max(1, query?.Page ?? 1);
        var pageSize = Math.Clamp(query?.PageSize ?? DefaultHistoryPageSize, 1, ProtocolConstants.MaxPageSize);
        if (page > 1 && page - 1 > int.MaxValue / pageSize)
        {
            throw new UnsupportedQueryException("getPlaybackHistory page offset overflows.");
        }

        var offset = (page - 1) * pageSize;
        var filter = PlaybackHistoryFilter.Build(query, search, "playback_history", PlaybackHistoryFilterKind.Events);
        var snapshot = query?.Snapshot is long requested && requested >= 0
            ? requested
            : Convert.ToInt64(CachedQuery.Scalar(database, "SELECT COALESCE(MAX(rowid), 0) FROM playback_history") ?? 0);
        var snapshotFilter = filter.Append("playback_history.rowid <= ?", snapshot);

        var total = Convert.ToInt64(CachedQuery.Scalar(database, $"""
            SELECT COUNT(*) FROM playback_history
            {snapshotFilter.WhereSql}
            """, snapshotFilter.Bind) ?? 0);
        var items = new List<PlaybackHistoryEntryDto>();
        using var reader = CachedQuery.Reader(database, $"""
            SELECT
              id, track_id, track_path, media_type, provider, provider_track_id,
              stable_key, title_snapshot, artist_snapshot, album_snapshot, duration_snapshot,
              title, artist, album, album_artist, started_at, ended_at, played_seconds,
              duration_seconds, completed, source_type, source_label, queue_id
            FROM playback_history
            {snapshotFilter.WhereSql}
            ORDER BY {PlaybackHistoryOrder.Build(query, "records")}
            LIMIT ? OFFSET ?
            """, command =>
        {
            snapshotFilter.Bind(command);
            command.Add(pageSize);
            command.Add(offset);
        });
        while (reader.Read())
        {
            items.Add(MapRecord(reader));
        }
        reader.Dispose();
        var daySummaries = PlaybackHistoryDaySummaryQuery.Execute(database, items, snapshotFilter);

        return new LibraryPageDto<PlaybackHistoryEntryDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total,
            HasMore = offset + items.Count < total,
            Snapshot = snapshot,
            DaySummaries = daySummaries,
        };
    }

    private static PlaybackHistoryEntryDto MapRecord(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var mediaType = Text(reader, 3);
        return new PlaybackHistoryEntryDto
        {
            Id = Text(reader, 0) ?? string.Empty,
            TrackId = Text(reader, 1),
            TrackPath = Text(reader, 2) ?? string.Empty,
            MediaType = mediaType is "remote" or "streaming" ? mediaType : "local",
            Provider = Text(reader, 4),
            ProviderTrackId = Text(reader, 5),
            StableKey = Text(reader, 6),
            Title = Text(reader, 7) ?? Text(reader, 11) ?? string.Empty,
            Artist = Text(reader, 8) ?? Text(reader, 12) ?? string.Empty,
            Album = Text(reader, 9) ?? Text(reader, 13) ?? string.Empty,
            AlbumArtist = Text(reader, 14) ?? string.Empty,
            CoverId = null,
            CoverThumb = null,
            StartedAt = Text(reader, 15) ?? string.Empty,
            EndedAt = Text(reader, 16),
            PlayedSeconds = Real(reader, 17),
            DurationSeconds = Real(reader, 18),
            DurationSnapshot = reader.IsDBNull(10) ? null : Convert.ToDouble(reader.GetValue(10)),
            CoverSnapshot = null,
            PlayCount = 1,
            Completed = Int(reader, 19) > 0,
            SourceType = Text(reader, 20),
            SourceLabel = Text(reader, 21),
            QueueId = Text(reader, 22),
        };
    }

    private static string? Text(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static int Int(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static double Real(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToDouble(reader.GetValue(ordinal));
}
