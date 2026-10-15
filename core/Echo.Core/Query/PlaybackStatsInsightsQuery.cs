using System.Globalization;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class PlaybackStatsInsightsQuery
{
    private const int SessionGapSeconds = 30 * 60;
    private const int RediscoveryGapDays = 45;
    private const string HistoryKeySql = "COALESCE(history.stable_key, history.track_id, history.track_path)";

    public static PlaybackStatsInsightsDto Execute(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query)
    {
        if (!QueryCapabilities.SupportsPlaybackStatsInsights(query))
        {
            throw new UnsupportedQueryException("getPlaybackStatsInsights combination is not migrated.");
        }

        using var transaction = database.BeginDeferredRead();
        return new PlaybackStatsInsightsDto
        {
            Comparison = PeriodComparison(database, query),
            Sessions = SessionSummary(database, query),
            Discovery = DiscoverySummary(database, query),
        };
    }

    private static PlaybackHistoryFilter InsightsFilter(CorePlaybackHistoryQuery? query, bool includeRange) =>
        PlaybackHistoryFilter.Build(
            query,
            null,
            "history",
            PlaybackHistoryFilterKind.Insights,
            includeRange,
            includeSearch: false);

    private static PlaybackStatsInsightsComparisonDto? PeriodComparison(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query)
    {
        if (!DateTimeOffset.TryParse(query?.From, out var from)
            || !DateTimeOffset.TryParse(query?.To, out var to)
            || to <= from)
        {
            return null;
        }

        var span = to - from;
        var previousFrom = from.Subtract(span).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return new PlaybackStatsInsightsComparisonDto
        {
            Current = AggregatePeriod(database, query, query!.From!, query.To!),
            Previous = AggregatePeriod(database, query, previousFrom, query.From!),
        };
    }

    private static PlaybackStatsPeriodTotalsDto AggregatePeriod(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        string from,
        string to)
    {
        var filter = InsightsFilter(query, includeRange: false)
            .Append("history.started_at >= ?", from)
            .Append("history.started_at < ?", to);
        using var reader = CachedQuery.Reader(database, $"""
            SELECT
              COUNT(*),
              COALESCE(SUM(history.played_seconds), 0),
              COUNT(DISTINCT {HistoryKeySql})
            FROM playback_history AS history
            {filter.WhereSql}
            """, filter.Bind);
        if (!reader.Read())
        {
            return new PlaybackStatsPeriodTotalsDto();
        }

        return new PlaybackStatsPeriodTotalsDto
        {
            PlayCount = Int(reader, 0),
            PlayedSeconds = Real(reader, 1),
            UniqueTracks = Int(reader, 2),
        };
    }

    private static PlaybackStatsInsightsSessionsDto SessionSummary(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query)
    {
        var filter = InsightsFilter(query, includeRange: true);
        int sessionCount = 0;
        double averagePlayed = 0;
        double longestPlayed = 0;
        double averageTrackCount = 0;
        using (var reader = CachedQuery.Reader(database, $"""
            WITH filtered AS (
              SELECT
                history.id,
                history.started_at,
                COALESCE(history.ended_at, history.started_at) AS ended_at,
                MAX(0, history.played_seconds) AS played_seconds
              FROM playback_history AS history
              {filter.WhereSql}
            ), ordered AS (
              SELECT
                filtered.*,
                LAG(filtered.ended_at) OVER (ORDER BY filtered.started_at, filtered.id) AS previous_ended_at
              FROM filtered
            ), marked AS (
              SELECT
                ordered.*,
                CASE
                  WHEN previous_ended_at IS NULL THEN 1
                  WHEN (julianday(started_at) - julianday(previous_ended_at)) * 86400 > {SessionGapSeconds} THEN 1
                  ELSE 0
                END AS starts_session
              FROM ordered
            ), grouped AS (
              SELECT
                marked.*,
                SUM(starts_session) OVER (ORDER BY started_at, id) AS session_id
              FROM marked
            ), sessions AS (
              SELECT
                session_id,
                COUNT(*) AS track_count,
                SUM(played_seconds) AS played_seconds
              FROM grouped
              GROUP BY session_id
            )
            SELECT
              COUNT(*) AS session_count,
              COALESCE(AVG(played_seconds), 0) AS average_played_seconds,
              COALESCE(MAX(played_seconds), 0) AS longest_played_seconds,
              COALESCE(AVG(track_count), 0) AS average_track_count
            FROM sessions
            """, filter.Bind))
        {
            if (reader.Read())
            {
                sessionCount = Int(reader, 0);
                averagePlayed = Real(reader, 1);
                longestPlayed = Real(reader, 2);
                averageTrackCount = Real(reader, 3);
            }
        }

        var dates = new List<string>();
        using (var reader = CachedQuery.Reader(database, $"""
            SELECT DISTINCT substr(history.started_at, 1, 10) AS date
            FROM playback_history AS history
            {filter.WhereSql}
            ORDER BY date
            """, filter.Bind))
        {
            while (reader.Read())
            {
                var date = Text(reader, 0);
                if (!string.IsNullOrWhiteSpace(date))
                {
                    dates.Add(date);
                }
            }
        }

        var streaks = StreakSummary(dates);
        return new PlaybackStatsInsightsSessionsDto
        {
            SessionCount = sessionCount,
            AveragePlayedSeconds = averagePlayed,
            LongestPlayedSeconds = longestPlayed,
            AverageTrackCount = averageTrackCount,
            ActiveDays = dates.Count,
            CurrentStreakDays = streaks.Current,
            LongestStreakDays = streaks.Longest,
        };
    }

    private static (int Current, int Longest) StreakSummary(IReadOnlyList<string> dates)
    {
        var sorted = dates.Where(static date => !string.IsNullOrWhiteSpace(date)).Distinct().OrderBy(static date => date).ToArray();
        if (sorted.Length == 0)
        {
            return (0, 0);
        }

        var longest = 1;
        var running = 1;
        for (var index = 1; index < sorted.Length; index += 1)
        {
            var previous = DateTimeOffset.Parse($"{sorted[index - 1]}T00:00:00.000Z");
            var current = DateTimeOffset.Parse($"{sorted[index]}T00:00:00.000Z");
            running = current - previous == TimeSpan.FromDays(1) ? running + 1 : 1;
            longest = Math.Max(longest, running);
        }

        var todayKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var yesterdayKey = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        var latest = sorted[^1];
        if (latest != todayKey && latest != yesterdayKey)
        {
            return (0, longest);
        }

        var currentStreak = 1;
        for (var index = sorted.Length - 1; index > 0; index -= 1)
        {
            var right = DateTimeOffset.Parse($"{sorted[index]}T00:00:00.000Z");
            var left = DateTimeOffset.Parse($"{sorted[index - 1]}T00:00:00.000Z");
            if (right - left != TimeSpan.FromDays(1))
            {
                break;
            }

            currentStreak += 1;
        }

        return (currentStreak, longest);
    }

    private static PlaybackStatsInsightsDiscoveryDto DiscoverySummary(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query)
    {
        var currentFilter = InsightsFilter(query, includeRange: true);
        var lifetimeFilter = InsightsFilter(query, includeRange: false);
        var from = string.IsNullOrWhiteSpace(query?.From) ? null : query.From;
        var to = string.IsNullOrWhiteSpace(query?.To) ? null : query.To;
        int uniqueTracks = 0;
        int playCount = 0;
        int replayedTracks = 0;
        int newTracks = 0;
        int returningTracks = 0;
        using (var reader = CachedQuery.Reader(database, $"""
            WITH current_tracks AS (
              SELECT {HistoryKeySql} AS history_key, COUNT(*) AS play_count
              FROM playback_history AS history
              {currentFilter.WhereSql}
              GROUP BY history_key
            ), lifetime_tracks AS (
              SELECT {HistoryKeySql} AS history_key, MIN(history.started_at) AS first_played_at
              FROM playback_history AS history
              {lifetimeFilter.WhereSql}
              GROUP BY history_key
            )
            SELECT
              COUNT(*) AS unique_tracks,
              COALESCE(SUM(current_tracks.play_count), 0) AS play_count,
              COALESCE(SUM(CASE WHEN current_tracks.play_count > 1 THEN 1 ELSE 0 END), 0) AS replayed_tracks,
              COALESCE(SUM(CASE WHEN lifetime_tracks.first_played_at >= COALESCE(?, lifetime_tracks.first_played_at) THEN 1 ELSE 0 END), 0) AS new_tracks,
              COALESCE(SUM(CASE WHEN ? IS NOT NULL AND lifetime_tracks.first_played_at < ? THEN 1 ELSE 0 END), 0) AS returning_tracks
            FROM current_tracks
            INNER JOIN lifetime_tracks ON lifetime_tracks.history_key = current_tracks.history_key
            """, command =>
        {
            currentFilter.Bind(command);
            lifetimeFilter.Bind(command);
            command.Add(from);
            command.Add(from);
            command.Add(from);
        }))
        {
            if (reader.Read())
            {
                uniqueTracks = Int(reader, 0);
                playCount = Int(reader, 1);
                replayedTracks = Int(reader, 2);
                newTracks = Int(reader, 3);
                returningTracks = Int(reader, 4);
            }
        }

        var timeline = InsightsFilter(query, includeRange: false);
        var outer = new PlaybackHistoryFilter(string.Empty, []);
        if (from is not null)
        {
            outer = outer.Append("started_at >= ?", from);
        }

        if (to is not null)
        {
            outer = outer.Append("started_at < ?", to);
        }

        outer = outer
            .Append("previous_started_at IS NOT NULL")
            .Append($"(julianday(started_at) - julianday(previous_started_at)) >= {RediscoveryGapDays}");

        var rediscovered = 0;
        using (var reader = CachedQuery.Reader(database, $"""
            WITH timeline AS (
              SELECT
                {HistoryKeySql} AS history_key,
                history.started_at,
                LAG(history.started_at) OVER (
                  PARTITION BY {HistoryKeySql}
                  ORDER BY history.started_at, history.id
                ) AS previous_started_at
              FROM playback_history AS history
              {timeline.WhereSql}
            )
            SELECT COUNT(DISTINCT history_key)
            FROM timeline
            {outer.WhereSql}
            """, command =>
        {
            timeline.Bind(command);
            outer.Bind(command);
        }))
        {
            if (reader.Read())
            {
                rediscovered = Int(reader, 0);
            }
        }

        return new PlaybackStatsInsightsDiscoveryDto
        {
            HasBoundedRange = from is not null && to is not null,
            NewTrackCount = from is not null ? newTracks : uniqueTracks,
            ReturningTrackCount = from is not null ? returningTracks : 0,
            ReplayedTrackCount = replayedTracks,
            RepeatPlayRate = playCount > 0 ? Math.Max(0, playCount - uniqueTracks) / (double)playCount : 0,
            RediscoveredTrackCount = rediscovered,
        };
    }

    private static string? Text(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static int Int(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static double Real(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToDouble(reader.GetValue(ordinal));
}
