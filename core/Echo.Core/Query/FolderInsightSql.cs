namespace Echo.Core.Query;

public static class FolderInsightSql
{
    public static string? For(string? insight, string table = "tracks")
    {
        return insight switch
        {
            "recentlyAdded" => $"""
                {table}.created_at >= COALESCE((
                  SELECT scan_jobs.started_at
                  FROM scan_jobs
                  WHERE scan_jobs.folder_id = {table}.folder_id
                    AND scan_jobs.status = 'completed'
                    AND scan_jobs.started_at IS NOT NULL
                  ORDER BY COALESCE(scan_jobs.finished_at, scan_jobs.updated_at, scan_jobs.created_at) DESC
                  LIMIT 1
                ), '9999-12-31T23:59:59.999Z')
                """,
            "unplayed" => $"COALESCE({table}.play_count, 0) = 0",
            "lossless" => $"UPPER(COALESCE({table}.codec, '')) IN ('FLAC', 'ALAC', 'WAV', 'AIFF', 'APE', 'DSF', 'DFF')",
            "hiRes" => $"((COALESCE({table}.bit_depth, 0) >= 24 AND COALESCE({table}.sample_rate, 0) >= 88200) OR UPPER(COALESCE({table}.codec, '')) IN ('DSF', 'DFF', 'DSD'))",
            "missingCover" => $"""
                NOT EXISTS (
                  SELECT 1 FROM covers
                  WHERE covers.id = {table}.cover_id
                    AND covers.source_type != 'default'
                )
                """,
            "missingLyrics" => $"""
                NOT EXISTS (
                  SELECT 1 FROM lyrics_cache
                  WHERE lyrics_cache.track_id = {table}.id
                    AND lyrics_cache.kind IN ('plain', 'synced', 'instrumental')
                )
                """,
            "duplicates" => $"""
                EXISTS (
                  SELECT 1
                  FROM duplicate_track_members
                  INNER JOIN duplicate_track_groups ON duplicate_track_groups.id = duplicate_track_members.group_id
                  WHERE duplicate_track_members.track_id = {table}.id
                    AND duplicate_track_groups.mode = 'strict'
                )
                """,
            _ => null,
        };
    }
}
