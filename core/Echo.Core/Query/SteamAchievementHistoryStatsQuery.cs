using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class SteamAchievementHistoryStatsQuery
{
    public static SteamAchievementHistoryStatsDto Execute(ReadOnlyLibraryDatabase database)
    {
        using var transaction = database.BeginDeferredRead();
        return new SteamAchievementHistoryStatsDto
        {
            TotalPlayedSeconds = Real(database, """
                SELECT COALESCE(SUM(played_seconds), 0)
                FROM steam_achievement_playback_facts
                """),
            CompletedUniqueTracks = Int(database, """
                SELECT COUNT(DISTINCT CASE WHEN qualified_completion > 0 THEN track_id END)
                FROM steam_achievement_playback_facts
                """),
            QualifiedCompletedPlayCount = Int(database, """
                SELECT COALESCE(SUM(CASE WHEN qualified_completion > 0 THEN 1 ELSE 0 END), 0)
                FROM steam_achievement_playback_facts
                """),
            LongestCompletionStreakDays = Int(database, """
                WITH completion_days AS (
                  SELECT DISTINCT date(ended_at, 'localtime') AS active_date
                  FROM steam_achievement_playback_facts
                  WHERE qualified_completion > 0
                ), grouped_days AS (
                  SELECT active_date, julianday(active_date) - ROW_NUMBER() OVER (ORDER BY active_date) AS streak_group
                  FROM completion_days
                ), streaks AS (
                  SELECT COUNT(*) AS streak_days FROM grouped_days GROUP BY streak_group
                )
                SELECT COALESCE(MAX(streak_days), 0) FROM streaks
                """),
            NightPlayedSeconds = Real(database, """
                SELECT COALESCE(SUM(played_seconds), 0)
                FROM steam_achievement_playback_facts
                WHERE strftime('%H', started_at, 'localtime') IN ('00', '01', '02', '03', '04')
                """),
            HasFavoriteAlbum = Int(database, """
                WITH album_track_completions AS (
                  SELECT album_tracks.album_id, album_tracks.track_id, COUNT(facts.id) AS completed_count
                  FROM album_tracks
                  INNER JOIN tracks ON tracks.id = album_tracks.track_id AND tracks.missing = 0 AND tracks.library_hidden = 0
                  LEFT JOIN steam_achievement_playback_facts AS facts
                    ON facts.track_id = album_tracks.track_id AND facts.qualified_completion > 0
                  GROUP BY album_tracks.album_id, album_tracks.track_id
                ), qualifying_albums AS (
                  SELECT album_id FROM album_track_completions GROUP BY album_id
                  HAVING COUNT(*) >= 4 AND MIN(completed_count) >= 3
                )
                SELECT EXISTS(SELECT 1 FROM qualifying_albums)
                """) > 0,
            CompletedShortUniqueTracks = Int(database, """
                SELECT COUNT(DISTINCT facts.track_id)
                FROM steam_achievement_playback_facts AS facts
                INNER JOIN tracks ON tracks.id = facts.track_id AND tracks.missing = 0 AND tracks.library_hidden = 0
                WHERE facts.qualified_completion > 0
                  AND COALESCE(tracks.duration, 0) > 0
                  AND tracks.duration <= 60
                """),
            HasCompletedZhaoXiaoliuTrack = Int(database, """
                SELECT EXISTS(
                  SELECT 1 FROM steam_achievement_playback_facts
                  WHERE qualified_completion > 0 AND INSTR(artist, '赵小六') > 0
                )
                """) > 0,
            CompletedUniqueAlbums = Int(database, """
                SELECT COUNT(DISTINCT album_tracks.album_id)
                FROM steam_achievement_playback_facts AS facts
                INNER JOIN album_tracks ON album_tracks.track_id = facts.track_id
                INNER JOIN tracks ON tracks.id = facts.track_id AND tracks.missing = 0 AND tracks.library_hidden = 0
                WHERE facts.qualified_completion > 0
                """),
            CompletedUniqueArtists = Int(database, """
                SELECT COUNT(DISTINCT artist_tracks.artist_id)
                FROM steam_achievement_playback_facts AS facts
                INNER JOIN artist_tracks ON artist_tracks.track_id = facts.track_id
                INNER JOIN tracks ON tracks.id = facts.track_id AND tracks.missing = 0 AND tracks.library_hidden = 0
                WHERE facts.qualified_completion > 0
                """),
            CompletedTimeBucketCount = Int(database, """
                SELECT COUNT(DISTINCT CAST(strftime('%H', ended_at, 'localtime') AS INTEGER) / 4)
                FROM steam_achievement_playback_facts
                WHERE qualified_completion > 0
                """),
            CompletedLongUniqueTracks = Int(database, """
                SELECT COUNT(DISTINCT track_id)
                FROM steam_achievement_playback_facts
                WHERE qualified_completion > 0 AND duration_seconds >= 360
                """),
            MaxActiveDatesInRolling30Days = Int(database, """
                WITH completion_days AS (
                  SELECT DISTINCT date(ended_at, 'localtime') AS active_date
                  FROM steam_achievement_playback_facts
                  WHERE qualified_completion > 0
                ), rolling_windows AS (
                  SELECT anchor.active_date, COUNT(candidate.active_date) AS active_dates
                  FROM completion_days AS anchor
                  INNER JOIN completion_days AS candidate
                    ON julianday(candidate.active_date) BETWEEN julianday(anchor.active_date) - 29 AND julianday(anchor.active_date)
                  GROUP BY anchor.active_date
                )
                SELECT COALESCE(MAX(active_dates), 0) FROM rolling_windows
                """),
        };
    }

    private static int Int(ReadOnlyLibraryDatabase database, string sql) =>
        Convert.ToInt32(CachedQuery.Scalar(database, sql) ?? 0);

    private static double Real(ReadOnlyLibraryDatabase database, string sql) =>
        Convert.ToDouble(CachedQuery.Scalar(database, sql) ?? 0);
}
