namespace Echo.Core.Query.Sql;

public static class TrackSelectSql
{
    public const string Columns = """
        tracks.id,
        tracks.path,
        tracks.title,
        tracks.artist,
        tracks.album,
        tracks.album_artist,
        tracks.track_no,
        tracks.disc_no,
        tracks.year,
        tracks.genre,
        tracks.duration,
        tracks.codec,
        tracks.sample_rate,
        tracks.bit_depth,
        tracks.bitrate,
        tracks.bpm,
        tracks.bpm_confidence,
        tracks.beat_offset_ms,
        tracks.analysis_status,
        tracks.analysis_updated_at,
        tracks.replay_gain_track_gain_db,
        tracks.replay_gain_album_gain_db,
        tracks.replay_gain_track_peak,
        tracks.replay_gain_album_peak,
        tracks.replay_gain_integrated_lufs,
        tracks.replay_gain_source,
        tracks.replay_gain_status,
        tracks.replay_gain_updated_at,
        tracks.cover_id,
        tracks.created_at,
        tracks.recommendation_preference,
        tracks.play_count,
        tracks.skip_count,
        tracks.last_played_at,
        tracks.last_skipped_at,
        tracks.metadata_status,
        tracks.embedded_metadata_status,
        tracks.embedded_cover_status,
        tracks.network_metadata_status,
        tracks.field_sources_json
        """;

    public const string ListColumns = """
        tracks.id,
        tracks.title,
        tracks.artist,
        tracks.album,
        tracks.track_no,
        tracks.disc_no,
        tracks.genre,
        tracks.path AS list_path,
        CASE WHEN tracks.replay_gain_track_gain_db IS NOT NULL
              OR tracks.replay_gain_album_gain_db IS NOT NULL
             THEN 1 ELSE 0 END AS has_replay_gain,
        tracks.duration,
        tracks.codec,
        tracks.sample_rate,
        tracks.bit_depth,
        tracks.bitrate,
        tracks.bpm,
        tracks.bpm_confidence,
        tracks.analysis_status,
        tracks.cover_id,
        json_extract(tracks.field_sources_json, '$.mqa') AS mqa_source,
        json_extract(tracks.field_sources_json, '$.bpm') AS bpm_source,
        json_extract(tracks.field_sources_json, '$.osuStoryboard') AS osu_storyboard_source
        """;

    public static string ForList(bool list) => (list ? ListColumns : Columns) + VocalPresenceSql.LocalColumn;
}
