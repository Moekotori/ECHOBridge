using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class AlbumFocusSql
{
    public static bool IsActive(CoreAlbumFocus? focus)
    {
        if (focus is null)
        {
            return false;
        }

        return AlbumFocusCriteria.Read(focus).Count > 0 || focus.Listening is "unplayed" or "notPlayed365";
    }

    public static string AudioFormatCaseSql() => """
        CASE
          WHEN (UPPER(COALESCE(tracks.codec, '')) IN ('DSF', 'DFF', 'DSD') OR COALESCE(tracks.sample_rate, 0) >= 1000000) THEN 'dsd'
          WHEN tracks.sample_rate = 44100 THEN 'sampleRate44100'
          WHEN tracks.sample_rate = 48000 THEN 'sampleRate48000'
          WHEN tracks.sample_rate = 88200 THEN 'sampleRate88200'
          WHEN tracks.sample_rate = 96000 THEN 'sampleRate96000'
          WHEN tracks.sample_rate = 176400 THEN 'sampleRate176400'
          WHEN tracks.sample_rate = 192000 THEN 'sampleRate192000'
          WHEN tracks.sample_rate = 352800 THEN 'sampleRate352800'
          WHEN tracks.sample_rate = 384000 THEN 'sampleRate384000'
          WHEN COALESCE(tracks.sample_rate, 0) > 0 THEN 'otherSampleRate'
          ELSE NULL
        END
        """;

    public static (string Sql, List<object> Parameters) WhereSql(CoreAlbumFocus? focus)
    {
        if (!IsActive(focus) || focus is null)
        {
            return (string.Empty, []);
        }

        var parts = new List<string>();
        var parameters = new List<object>();
        (string Sql, List<object> Parameters) Predicate(FocusCriterion item)
        {
            var parameters = new List<object>();
            string sql;
            switch (item.Field)
            {
                case "decade":
                    sql = "(albums.year >= ? AND albums.year <= ?)";
                    parameters.Add(item.Value); parameters.Add((int)item.Value + 9);
                    if (item.Exclude) sql = $"NOT COALESCE({sql}, 0)";
                    break;
                case "genre":
                    sql = "echo_genre_key(tracks.genre) = ?"; parameters.Add(item.Value); break;
                case "audioFormat":
                    sql = SqlFragments.AudioFormatSql((string)item.Value, TrackFilterColumns.Local); break;
                case "bitDepth": sql = "COALESCE(tracks.bit_depth, 0) >= 24"; break;
                case "fileFormat": sql = SqlFragments.FileFormatSql("flac", TrackFilterColumns.Local); break;
                default:
                    sql = """
                        EXISTS (SELECT 1 FROM music_credits AS credit
                          JOIN music_credit_roles AS role ON role.id = credit.role_id
                          WHERE credit.track_id = tracks.id AND credit.person_id = ? AND role.role_key = ?)
                        """;
                    parameters.Add(item.Value); parameters.Add(item.Field); break;
            }
            return (sql, parameters);
        }
        foreach (var group in AlbumFocusCriteria.Read(focus).GroupBy(item => item.Field))
        {
            var alternatives = group.Where(item => !item.Exclude).Select(Predicate).ToList();
            if (alternatives.Count > 0)
            {
                var anyMatch = "(" + string.Join(" OR ", alternatives.Select(item => item.Sql)) + ")";
                parts.Add(group.Key == "decade" ? anyMatch : MatchingAlbumTracksSql(anyMatch));
                parameters.AddRange(alternatives.SelectMany(item => item.Parameters));
            }
            foreach (var item in group.Where(item => item.Exclude))
            {
                var negative = Predicate(item);
                parts.Add(group.Key == "decade" ? negative.Sql : "NOT (" + MatchingAlbumTracksSql(negative.Sql) + ")");
                parameters.AddRange(negative.Parameters);
            }
        }
        if (focus.Listening is "unplayed" or "notPlayed365")
        {
            var recent = focus.Listening == "notPlayed365";
            // Keep heard-album membership outside the correlated track check so SQLite builds it once.
            parts.Add(MatchingAlbumTracksSql("1"));
            parts.Add($"""
                albums.id NOT IN (SELECT album_tracks.album_id FROM playback_history_stats AS history
                  JOIN tracks ON tracks.id = history.track_id
                  JOIN album_tracks ON album_tracks.track_id = tracks.id
                  WHERE history.media_type = 'local' AND history.play_count > 0
                    AND tracks.missing = 0 AND tracks.library_hidden = 0
                    {(recent ? "AND history.last_started_at >= ?" : "")})
                """);
            if (recent) parameters.Add(DateTimeOffset.UtcNow.AddDays(-365).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
        }

        return (string.Join(" AND ", parts), parameters);
    }

    private static string MatchingAlbumTracksSql(string predicateSql) => $"""
        EXISTS (
          SELECT 1
          FROM album_tracks
          INNER JOIN tracks ON tracks.id = album_tracks.track_id
          WHERE album_tracks.album_id = albums.id
            AND tracks.missing = 0 AND tracks.library_hidden = 0
            AND ({predicateSql})
        )
        """;
}
