using System.Globalization;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class PlaybackHistorySummaryQuery
{
    public static PlaybackHistorySummaryDto Execute(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search)
    {
        if (!QueryCapabilities.SupportsPlaybackHistorySummary(query))
        {
            throw new UnsupportedQueryException("getPlaybackHistorySummary combination is not migrated.");
        }

        using var transaction = database.BeginDeferredRead();
        var now = DateTime.Now;
        var startOfToday = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Local);
        var endOfToday = startOfToday.AddDays(1);
        var todayStartIso = UtcIso(startOfToday);
        var todayEndIso = UtcIso(endOfToday);
        var todayFilter = PlaybackHistoryFilter.Build(
            query,
            search,
            "playback_history",
            PlaybackHistoryFilterKind.Events,
            includeRange: false,
            includeSearch: false);
        var todayScoped = todayFilter.Append("started_at >= ?", todayStartIso).Append("started_at < ?", todayEndIso);

        int todayCount = 0;
        double todayPlayed = 0;
        using (var reader = CachedQuery.Reader(database, $"""
            SELECT COUNT(*) AS count, COALESCE(SUM(played_seconds), 0) AS played_seconds
            FROM playback_history
            {todayScoped.WhereSql}
            """, todayScoped.Bind))
        {
            if (reader.Read())
            {
                todayCount = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                todayPlayed = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1));
            }
        }

        if (PlaybackHistoryFilter.IsUnfiltered(query))
        {
            using var reader = CachedQuery.Reader(database, """
                SELECT
                  COALESCE(SUM(play_count), 0) AS count,
                  COUNT(*) AS track_count,
                  COALESCE(SUM(total_played_seconds), 0) AS played_seconds,
                  MAX(last_started_at) AS latest
                FROM playback_history_stats
                """);
            if (!reader.Read())
            {
                return Empty(todayCount, todayPlayed);
            }

            var totalCount = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
            var latest = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3));
            return new PlaybackHistorySummaryDto
            {
                TodayCount = todayCount,
                TodayPlayedSeconds = todayPlayed,
                TotalCount = totalCount,
                LatestPlayedAt = latest,
                RangeCount = totalCount,
                RangeTrackCount = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                RangePlayedSeconds = reader.IsDBNull(2) ? 0 : Convert.ToDouble(reader.GetValue(2)),
                RangeLatestPlayedAt = latest,
            };
        }

        var sourceOnly = PlaybackHistoryFilter.Build(
            query,
            search,
            "playback_history",
            PlaybackHistoryFilterKind.Events,
            includeRange: false,
            includeSearch: false,
            includeMedia: false);
        int total = 0;
        string? latestPlayed = null;
        using (var reader = CachedQuery.Reader(database, $"""
            SELECT COUNT(*) AS total, MAX(started_at) AS latest
            FROM playback_history
            {sourceOnly.WhereSql}
            """, sourceOnly.Bind))
        {
            if (reader.Read())
            {
                total = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                latestPlayed = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1));
            }
        }

        var rangeFilter = PlaybackHistoryFilter.Build(query, search, "playback_history", PlaybackHistoryFilterKind.Events);
        using (var reader = CachedQuery.Reader(database, $"""
            SELECT
              COUNT(*) AS count,
              COUNT(DISTINCT COALESCE(stable_key, track_id, track_path)) AS track_count,
              COALESCE(SUM(played_seconds), 0) AS played_seconds,
              MAX(started_at) AS latest
            FROM playback_history
            {rangeFilter.WhereSql}
            """, rangeFilter.Bind))
        {
            if (!reader.Read())
            {
                return new PlaybackHistorySummaryDto
                {
                    TodayCount = todayCount,
                    TodayPlayedSeconds = todayPlayed,
                    TotalCount = total,
                    LatestPlayedAt = latestPlayed,
                };
            }

            return new PlaybackHistorySummaryDto
            {
                TodayCount = todayCount,
                TodayPlayedSeconds = todayPlayed,
                TotalCount = total,
                LatestPlayedAt = latestPlayed,
                RangeCount = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                RangeTrackCount = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                RangePlayedSeconds = reader.IsDBNull(2) ? 0 : Convert.ToDouble(reader.GetValue(2)),
                RangeLatestPlayedAt = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3)),
            };
        }
    }

    private static PlaybackHistorySummaryDto Empty(int todayCount, double todayPlayed) =>
        new()
        {
            TodayCount = todayCount,
            TodayPlayedSeconds = todayPlayed,
        };

    private static string UtcIso(DateTime local) =>
        local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
