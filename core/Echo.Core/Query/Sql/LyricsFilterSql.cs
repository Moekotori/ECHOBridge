namespace Echo.Core.Query.Sql;

public static class LyricsFilterSql
{
    public const string Capability = "getTracks.lyricsFilter";

    // The shared SQL migration maintains this index on cache writes.
    public static string Filter(string? filter, string trackId)
    {
        if (filter is null or "all") return string.Empty;

        var state = filter switch
        {
            "withWordTiming" => "= 2",
            "withoutWordTiming" => "= 1",
            _ => "IN (1, 2)",
        };
        var hasLyrics = $"""
            EXISTS (SELECT 1 FROM track_lyrics_filter
              WHERE track_id = {trackId} AND state {state})
            """;
        return filter == "withoutLyrics" ? $"NOT {hasLyrics}" : hasLyrics;
    }
}
