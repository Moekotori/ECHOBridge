namespace Echo.Core.Query.Sql;

public static class TrackOrderSql
{
    private static readonly HashSet<string> SqlWords = new(StringComparer.Ordinal)
    {
        "ORDER", "BY", "COLLATE", "NOCASE", "ASC", "DESC", "COALESCE", "IS", "NULL", "AND", "OR", "NOT", "RANDOM",
    };
    public static string IndexedTitleOrder { get; } = """
        sort_keys.title_key COLLATE NOCASE ASC,
        sort_keys.title_text COLLATE NOCASE ASC,
        sort_keys.artist_key COLLATE NOCASE ASC,
        sort_keys.artist_text COLLATE NOCASE ASC,
        sort_keys.track_id ASC
        """;

    public static string ForSorts(IReadOnlyList<string> sorts, bool searchActive)
    {
        var active = sorts.Count > 0 ? sorts : ["default"];
        if (active[0] == "random")
        {
            return "ORDER BY RANDOM()";
        }

        var clauses = new List<string>();
        if (searchActive && QueryCapabilities.IsTitleSort(active[0]))
        {
            clauses.Add("search_rank ASC");
        }

        foreach (var sort in active)
        {
            clauses.AddRange(ClausesFor(sort));
        }

        clauses.AddRange(StableClauses(active[0]));
        clauses.Add("title_key COLLATE NOCASE ASC");
        clauses.Add("title_text COLLATE NOCASE ASC");
        clauses.Add("artist_key COLLATE NOCASE ASC");
        clauses.Add("artist_text COLLATE NOCASE ASC");
        clauses.Add("id ASC");
        return $"ORDER BY {string.Join(", ", clauses.Distinct())}";
    }

    /// <summary>
    /// Columns the search page must project before ORDER BY. Relevance still
    /// sorts the whole hit set, but unused track fields stay out of that sorter.
    /// </summary>
    public static string SearchHitSelect(
        IReadOnlyList<string> sorts,
        bool searchActive,
        string searchRankSql,
        string playCountSql,
        string lastPlayedSql)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal) { "id" };
        var order = ForSorts(sorts, searchActive);
        for (var index = 0; index < order.Length;)
        {
            var current = order[index];
            if (current is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_')
            {
                var start = index;
                index++;
                while (index < order.Length && (char.IsAsciiLetterOrDigit(order[index]) || order[index] == '_'))
                {
                    index++;
                }

                var word = order[start..index];
                if (!SqlWords.Contains(word))
                {
                    aliases.Add(word);
                }

                continue;
            }

            index++;
        }

        return string.Join(",\n", aliases.Select(alias => Column(alias, searchRankSql, playCountSql, lastPlayedSql)));
    }

    private static string Column(string alias, string searchRankSql, string playCountSql, string lastPlayedSql) =>
        alias switch
        {
            "id" => "tracks.id AS id",
            "search_rank" => $"{searchRankSql} AS search_rank",
            "title_key" => "sort_keys.title_key AS title_key",
            "title_text" => "sort_keys.title_text AS title_text",
            "artist_key" => "sort_keys.artist_key AS artist_key",
            "artist_text" => "sort_keys.artist_text AS artist_text",
            "album_key" => "sort_keys.album_key AS album_key",
            "album_text" => "sort_keys.album_text AS album_text",
            "disc_no" => "tracks.disc_no AS disc_no",
            "track_no" => "tracks.track_no AS track_no",
            "updated_at" => "tracks.updated_at AS updated_at",
            "created_at" => "tracks.created_at AS created_at",
            "last_played_at" => $"{lastPlayedSql} AS last_played_at",
            "play_count" => $"{playCountSql} AS play_count",
            "year" => "tracks.year AS year",
            "duration" => "tracks.duration AS duration",
            "mtime_ms" => "tracks.mtime_ms AS mtime_ms",
            "size_bytes" => "tracks.size_bytes AS size_bytes",
            "bitrate" => "tracks.bitrate AS bitrate",
            "codec" => "tracks.codec AS codec",
            "sample_rate" => "tracks.sample_rate AS sample_rate",
            "bit_depth" => "tracks.bit_depth AS bit_depth",
            "bpm" => "tracks.bpm AS bpm",
            "path" => "tracks.path AS path",
            _ => throw new InvalidOperationException($"Search page cannot sort by {alias}."),
        };

    public static bool NeedsPlaybackStats(IReadOnlyList<string> sorts) =>
        sorts.Any(static sort => sort is "lastPlayed" or "playCountAsc" or "playCountDesc" or "frequent");

    private static IEnumerable<string> ClausesFor(string sort) =>
        sort switch
        {
            "artist" => ["artist_key COLLATE NOCASE ASC", "artist_text COLLATE NOCASE ASC"],
            "artistDesc" => ["artist_key COLLATE NOCASE DESC", "artist_text COLLATE NOCASE DESC"],
            "artistAlbum" =>
            [
                "artist_key COLLATE NOCASE ASC",
                "artist_text COLLATE NOCASE ASC",
                "album_key COLLATE NOCASE ASC",
                "album_text COLLATE NOCASE ASC",
                "COALESCE(disc_no, 0) ASC",
                "COALESCE(track_no, 999999) ASC",
            ],
            "album" =>
            [
                "album_key COLLATE NOCASE ASC",
                "album_text COLLATE NOCASE ASC",
                "COALESCE(disc_no, 0) ASC",
                "COALESCE(track_no, 999999) ASC",
            ],
            "albumDesc" =>
            [
                "album_key COLLATE NOCASE DESC",
                "album_text COLLATE NOCASE DESC",
                "COALESCE(disc_no, 0) ASC",
                "COALESCE(track_no, 999999) ASC",
            ],
            "recent" => ["updated_at DESC"],
            "lastPlayed" => ["(last_played_at IS NULL) ASC", "last_played_at DESC"],
            "playCountAsc" => ["COALESCE(play_count, 0) ASC"],
            "playCountDesc" or "frequent" => ["COALESCE(play_count, 0) DESC"],
            "yearAsc" => ["(year IS NULL) ASC", "year ASC"],
            "yearDesc" => ["(year IS NULL) ASC", "year DESC"],
            "createdAsc" => ["created_at ASC"],
            "createdDesc" => ["created_at DESC"],
            "titleDesc" => ["title_key COLLATE NOCASE DESC", "title_text COLLATE NOCASE DESC"],
            "durationAsc" => ["duration ASC"],
            "durationDesc" => ["duration DESC"],
            "fileModifiedAsc" => ["mtime_ms ASC"],
            "fileModifiedDesc" => ["mtime_ms DESC"],
            "qualityAsc" => ["COALESCE(bitrate, 0) ASC", "size_bytes ASC"],
            "qualityDesc" => ["COALESCE(bitrate, 0) DESC", "size_bytes DESC"],
            "codecAsc" => ["COALESCE(codec, '') COLLATE NOCASE ASC"],
            "codecDesc" => ["COALESCE(codec, '') COLLATE NOCASE DESC"],
            "audioSpecAsc" =>
            [
                "(sample_rate IS NULL OR sample_rate <= 0) ASC",
                "sample_rate ASC",
                "(bit_depth IS NULL OR bit_depth <= 0) ASC",
                "bit_depth ASC",
            ],
            "audioSpecDesc" =>
            [
                "(sample_rate IS NULL OR sample_rate <= 0) ASC",
                "sample_rate DESC",
                "(bit_depth IS NULL OR bit_depth <= 0) ASC",
                "bit_depth DESC",
            ],
            "bitrateAsc" => ["COALESCE(bitrate, 0) ASC"],
            "bitrateDesc" => ["COALESCE(bitrate, 0) DESC"],
            "bpmAsc" => ["(bpm IS NULL OR bpm <= 0) ASC", "bpm ASC"],
            "bpmDesc" => ["(bpm IS NULL OR bpm <= 0) ASC", "bpm DESC"],
            "trackNumber" =>
            [
                "(track_no IS NULL) ASC",
                "COALESCE(disc_no, 1) ASC",
                "COALESCE(track_no, 0) ASC",
            ],
            _ => ["title_key COLLATE NOCASE ASC", "title_text COLLATE NOCASE ASC"],
        };

    private static IEnumerable<string> StableClauses(string sort) =>
        sort switch
        {
            "playCountAsc" or "playCountDesc" or "frequent" => ["last_played_at DESC"],
            "titleDesc" => ["artist_key COLLATE NOCASE ASC", "artist_text COLLATE NOCASE ASC"],
            "trackNumber" => ["title_key COLLATE NOCASE ASC", "path COLLATE NOCASE ASC"],
            _ => [],
        };
}
