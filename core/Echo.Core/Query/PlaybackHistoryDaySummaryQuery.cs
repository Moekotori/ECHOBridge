using System.Globalization;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class PlaybackHistoryDaySummaryQuery
{
    // The page bounds the number of dates returned; history rows stay in SQLite.
    public static List<PlaybackHistoryDaySummaryDto> Execute(
        ReadOnlyLibraryDatabase database,
        List<PlaybackHistoryEntryDto> items,
        PlaybackHistoryFilter filter)
    {
        var dates = items.Select(item => DateTimeOffset.TryParse(item.StartedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time) ? time.LocalDateTime.Date : (DateTime?)null)
            .Where(date => date.HasValue).Select(date => date!.Value).Distinct().Order().ToList();
        if (dates.Count == 0) return [];
        var scoped = filter.Append("started_at >= ?", UtcIso(dates[0]))
            .Append("started_at < ?", UtcIso(dates[^1].AddDays(1)));
        foreach (var date in dates) scoped.Parameters.Add(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var placeholders = string.Join(", ", dates.Select(_ => "?"));
        using var reader = CachedQuery.Reader(database, $"""
            SELECT date(started_at, 'localtime') AS date,
              COUNT(*) AS play_count,
              COUNT(DISTINCT COALESCE(stable_key, track_id, track_path)) AS unique_tracks,
              COALESCE(SUM(MAX(0, played_seconds)), 0) AS played_seconds,
              SUM(CASE WHEN ended_at IS NULL THEN 1 ELSE 0 END) AS pending_count
            FROM playback_history
            {scoped.WhereSql} AND date(started_at, 'localtime') IN ({placeholders})
            GROUP BY date(started_at, 'localtime') ORDER BY date DESC
            """, scoped.Bind);
        var summaries = new List<PlaybackHistoryDaySummaryDto>();
        while (reader.Read()) summaries.Add(new PlaybackHistoryDaySummaryDto
        {
            Date = reader.GetString(0), PlayCount = reader.GetInt64(1), UniqueTracks = reader.GetInt64(2),
            PlayedSeconds = reader.GetDouble(3), PendingCount = reader.GetInt64(4),
        });
        return summaries;
    }

    private static string UtcIso(DateTime date) =>
        DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
