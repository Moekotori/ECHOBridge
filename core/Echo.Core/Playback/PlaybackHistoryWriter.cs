using Echo.Core.Database;
using Echo.Core.Protocol;

namespace Echo.Core.Playback;

/// <summary>
/// SQL twin of the legacy <c>createPlaybackHistoryEntry</c> / <c>finishPlaybackHistoryEntry</c> /
/// <c>recordTrackPlayback</c> / <c>recordSkip</c>. Field semantics (history key, completion rule,
/// delta accounting on re-finish) are preserved so both writers yield identical rows.
/// </summary>
internal static class PlaybackHistoryWriter
{
    public static PlaybackCommandResult Start(TransactionSql sql, PlaybackStart start)
    {
        if (!Guid.TryParseExact(start.Id, "D", out _) || string.IsNullOrWhiteSpace(start.TrackPath) || string.IsNullOrWhiteSpace(start.StartedAt))
            throw new ArgumentException("Playback start requires id, trackPath and startedAt.");
        var durationSeconds = Math.Max(0, double.IsFinite(start.DurationSeconds) ? start.DurationSeconds : 0);
        var durationSnapshot = start.DurationSnapshot is { } snapshot && double.IsFinite(snapshot) ? snapshot : durationSeconds;
        var trackId = Text(start.TrackId);
        var stableKey = Text(start.StableKey);
        var historyKey = stableKey ?? trackId ?? start.TrackPath;
        var mediaType = string.IsNullOrEmpty(start.MediaType) ? "local" : start.MediaType;
        sql.Run("""
            INSERT INTO playback_history (
              id, track_id, track_path, media_type, provider, provider_track_id, stable_key,
              title_snapshot, artist_snapshot, album_snapshot, duration_snapshot, cover_snapshot,
              title, artist, album, album_artist, cover_id,
              started_at, ended_at, played_seconds, duration_seconds, completed,
              source_type, source_label, queue_id, created_at
            ) VALUES ($0, $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $7, $8, $9, $12, $13, $14, NULL, 0, $15, 0, $16, $17, $18, $14)
            """,
            start.Id, trackId, start.TrackPath, mediaType, Text(start.Provider), Text(start.ProviderTrackId), stableKey,
            start.Title, start.Artist, start.Album, durationSnapshot, Text(start.CoverSnapshot),
            start.AlbumArtist, Text(start.CoverId), start.StartedAt, durationSeconds,
            Text(start.SourceType), Text(start.SourceLabel), Text(start.QueueId));
        sql.Run("""
            INSERT INTO playback_history_stats (
              history_key, track_id, track_path, media_type, provider, provider_track_id, stable_key,
              title_snapshot, artist_snapshot, album_snapshot, duration_snapshot, cover_snapshot,
              title, artist, album, album_artist, cover_id,
              play_count, completed_count, total_played_seconds, duration_seconds,
              last_started_at, last_ended_at, source_type, source_label, queue_id, updated_at
            ) VALUES ($0, $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $7, $8, $9, $12, $13, 1, 0, 0, $14, $15, NULL, $16, $17, $18, $15)
            ON CONFLICT(history_key) DO UPDATE SET
              track_id = excluded.track_id, track_path = excluded.track_path, media_type = excluded.media_type,
              provider = excluded.provider, provider_track_id = excluded.provider_track_id, stable_key = excluded.stable_key,
              title_snapshot = excluded.title_snapshot, artist_snapshot = excluded.artist_snapshot, album_snapshot = excluded.album_snapshot,
              duration_snapshot = excluded.duration_snapshot, cover_snapshot = excluded.cover_snapshot,
              title = excluded.title, artist = excluded.artist, album = excluded.album, album_artist = excluded.album_artist, cover_id = excluded.cover_id,
              play_count = playback_history_stats.play_count + 1, duration_seconds = excluded.duration_seconds,
              last_started_at = excluded.last_started_at, source_type = excluded.source_type, source_label = excluded.source_label,
              queue_id = excluded.queue_id, updated_at = excluded.updated_at
            """,
            historyKey, trackId, start.TrackPath, mediaType, Text(start.Provider), Text(start.ProviderTrackId), stableKey,
            start.Title, start.Artist, start.Album, durationSnapshot, Text(start.CoverSnapshot),
            start.AlbumArtist, Text(start.CoverId), durationSeconds, start.StartedAt,
            Text(start.SourceType), Text(start.SourceLabel), Text(start.QueueId));
        return new(HistoryId: start.Id, Found: true);
    }

    public static PlaybackCommandResult Finish(TransactionSql sql, PlaybackFinish finish)
    {
        if (string.IsNullOrWhiteSpace(finish.HistoryId) || string.IsNullOrWhiteSpace(finish.EndedAt))
            throw new ArgumentException("Playback finish requires historyId and endedAt.");
        using var command = sql.Command("SELECT track_id, track_path, stable_key, duration_seconds, played_seconds, completed FROM playback_history WHERE id=$0", finish.HistoryId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new(HistoryId: finish.HistoryId, Found: false);
        var trackId = reader.IsDBNull(0) ? null : Text(reader.GetString(0));
        var trackPath = reader.GetString(1);
        var stableKey = reader.IsDBNull(2) ? null : Text(reader.GetString(2));
        var currentDuration = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
        var previousPlayed = Math.Max(0, reader.IsDBNull(4) ? 0 : reader.GetDouble(4));
        var wasCompleted = !reader.IsDBNull(5) && reader.GetInt64(5) == 1;
        reader.Close();

        var playedSeconds = Math.Max(0, double.IsFinite(finish.PlayedSeconds) ? finish.PlayedSeconds : 0);
        var durationSeconds = finish.DurationSeconds is { } requested && double.IsFinite(requested) && requested > 0 ? requested : currentDuration;
        var completed = finish.Completed ?? IsCompleted(playedSeconds, durationSeconds);
        var historyKey = stableKey ?? trackId ?? trackPath;

        sql.Run("""
            UPDATE playback_history SET ended_at = $0, played_seconds = $1, duration_seconds = $2,
              duration_snapshot = COALESCE(duration_snapshot, $2), completed = $3 WHERE id = $4
            """, finish.EndedAt, playedSeconds, durationSeconds, completed ? 1 : 0, finish.HistoryId);
        sql.Run("""
            UPDATE playback_history_stats SET
              total_played_seconds = MAX(0, COALESCE(total_played_seconds, 0) + $0),
              completed_count = MAX(0, COALESCE(completed_count, 0) + $1),
              duration_seconds = $2, duration_snapshot = COALESCE(duration_snapshot, $2),
              last_ended_at = $3, updated_at = $3
            WHERE history_key = $4
            """, playedSeconds - previousPlayed, (completed ? 1 : 0) - (wasCompleted ? 1 : 0), durationSeconds, finish.EndedAt, historyKey);
        if (completed && !wasCompleted && trackId is not null) PlayCount(sql, trackId, finish.EndedAt);
        if (finish.Skipped && trackId is not null)
            sql.Run("UPDATE tracks SET skip_count = COALESCE(skip_count, 0) + 1, last_skipped_at = $0, updated_at = $0 WHERE id = $1 AND missing = 0", finish.EndedAt, trackId);
        return new(HistoryId: finish.HistoryId, Found: true, Completed: completed);
    }

    public static PlaybackCommandResult PlayCount(TransactionSql sql, string trackId, string timestamp)
    {
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(timestamp)) throw new ArgumentException("playCount requires trackId and timestamp.");
        var changed = sql.Run("UPDATE tracks SET play_count = COALESCE(play_count, 0) + 1, last_played_at = $0 WHERE id = $1 AND missing = 0", timestamp, trackId);
        return new(Found: changed > 0);
    }

    /// <summary>Same rule as the legacy store: 30 s, or half the track when the duration is known.</summary>
    internal static bool IsCompleted(double playedSeconds, double durationSeconds) =>
        durationSeconds <= 0 ? playedSeconds >= 30 : playedSeconds >= 30 || playedSeconds >= durationSeconds * 0.5;

    private static string? Text(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
