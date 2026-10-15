using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query.Sql;

public static class SqlFragments
{
    public static string And(string sql, string extra)
    {
        if (string.IsNullOrWhiteSpace(extra))
        {
            return sql;
        }

        return string.IsNullOrWhiteSpace(sql) ? $"WHERE {extra}" : $"{sql} AND {extra}";
    }

    /// <summary>
    /// Duplicate-group membership for one track, aggregated once per query. The mode's groups are
    /// joined to their members a single time instead of probing an IN (subquery) list for every
    /// track row, which cost seconds on libraries with hundreds of duplicate groups. One row per
    /// track (hidden = MIN so a track stays visible if any of its memberships is visible).
    /// Binds one parameter: the duplicate mode.
    /// </summary>
    public static string DuplicateMembersJoin(string trackIdColumn) => $"""
        LEFT JOIN (
          SELECT member.track_id AS track_id, MIN(member.hidden) AS hidden
          FROM duplicate_track_members AS member
          INNER JOIN duplicate_track_groups AS duplicate_group
            ON duplicate_group.id = member.group_id AND duplicate_group.mode = ?
          GROUP BY member.track_id
        ) AS duplicate_members ON duplicate_members.track_id = {trackIdColumn}
        """;

    public static string Placeholders(int count) =>
        string.Join(", ", Enumerable.Repeat("?", count));

    public static string JsonEachIds => "IN (SELECT value FROM json_each(?))";

    public static void Add(this SqliteCommand command, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"p{command.Parameters.Count}";
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    public static void UseNamedPlaceholders(this SqliteCommand command)
    {
        var index = 0;
        command.CommandText = ReplaceQuestions(command.CommandText, () => $"@p{index++}");
    }

    private static string ReplaceQuestions(string sql, Func<string> nextName)
    {
        var builder = new System.Text.StringBuilder(sql.Length + 16);
        foreach (var character in sql)
        {
            if (character == '?')
            {
                builder.Append(nextName());
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    public static string AudioFormatSql(string? filter, TrackFilterColumns columns) =>
        filter switch
        {
            "sampleRate44100" => $"{columns.SampleRate} = 44100",
            "sampleRate48000" => $"{columns.SampleRate} = 48000",
            "sampleRate88200" => $"{columns.SampleRate} = 88200",
            "sampleRate96000" => $"{columns.SampleRate} = 96000",
            "sampleRate176400" => $"{columns.SampleRate} = 176400",
            "sampleRate192000" => $"{columns.SampleRate} = 192000",
            "sampleRate352800" => $"{columns.SampleRate} = 352800",
            "sampleRate384000" => $"{columns.SampleRate} = 384000",
            "dsd" => DsdSql(columns),
            "otherSampleRate" => $"COALESCE({columns.SampleRate}, 0) > 0 AND {columns.SampleRate} NOT IN (44100, 48000, 88200, 96000, 176400, 192000, 352800, 384000) AND NOT {DsdSql(columns)}",
            _ => string.Empty,
        };

    public static string FileFormatSql(string? filter, TrackFilterColumns columns)
    {
        var path = $"LOWER(COALESCE({columns.Path}, ''))";
        var codec = $"UPPER(TRIM(COALESCE({columns.Codec}, '')))";
        var codecRaw = $"UPPER(COALESCE({columns.Codec}, ''))";
        return filter switch
        {
            "flac" => $"({codec} = 'FLAC' OR {path} LIKE '%.flac')",
            "mp3" => $"({codec} = 'MP3' OR {codecRaw} LIKE 'MPEG%LAYER 3%' OR {codecRaw} LIKE 'MPEG%LAYER III%' OR {path} LIKE '%.mp3')",
            "wav" => $"({codec} IN ('WAV', 'WAVE') OR {path} LIKE '%.wav')",
            "aiff" => $"({codec} IN ('AIFF', 'AIF', 'AIFC') OR {path} LIKE '%.aiff' OR {path} LIKE '%.aif' OR {path} LIKE '%.aifc')",
            "alac" => $"({codec} IN ('ALAC', 'APPLE LOSSLESS') OR {path} LIKE '%.alac')",
            "aac" => $"({codec} IN ('AAC', 'MP4A') OR {codecRaw} LIKE 'AAC %' OR {path} LIKE '%.aac')",
            "ogg" => $"({codec} = 'OGG' OR {codecRaw} LIKE '%VORBIS%' OR {path} LIKE '%.ogg')",
            "opus" => $"({codec} = 'OPUS' OR {path} LIKE '%.opus')",
            "dsd" => DsdSql(columns),
            "other" => $"NOT ({string.Join(" OR ", new[] { "flac", "mp3", "wav", "aiff", "alac", "aac", "ogg", "opus" }.Select(item => FileFormatSql(item, columns)).Append(DsdSql(columns)))})",
            _ => string.Empty,
        };
    }

    public static string OsuImportedSql(TrackFilterColumns columns) =>
        $"(lower(trim(COALESCE(json_extract({columns.FieldSourcesJson}, '$.osu'), ''))) = 'osu' OR lower(trim(COALESCE({columns.Album}, ''))) LIKE 'osu! beatmapset %')";

    public static string ExcludeSql(TrackFilterColumns columns, IReadOnlyList<CoreSearchExclude> excludes)
    {
        if (excludes.Count == 0)
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        foreach (var exclude in excludes)
        {
            var columnSql = new[]
            {
                columns.Title,
                columns.Artist,
                columns.Album,
                columns.AlbumArtist,
                $"COALESCE({columns.Genre}, '')",
                columns.Path,
            };
            var parts = exclude.Variants.SelectMany(_ => columnSql.Select(column =>
                exclude.Substring
                    ? $"{column} LIKE ? ESCAPE '\\'"
                    : $"{ExcludeFoldSql(column)} LIKE ? ESCAPE '\\'"));
            clauses.Add($"NOT ({string.Join(" OR ", parts)})");
        }

        return string.Join(" AND ", clauses);
    }

    public static IEnumerable<object> ExcludeParams(IReadOnlyList<CoreSearchExclude> excludes)
    {
        foreach (var exclude in excludes)
        {
            foreach (var variant in exclude.Variants)
            {
                for (var index = 0; index < 6; index += 1)
                {
                    yield return exclude.Substring
                        ? $"%{EscapeLike(variant)}%"
                        : $"% {EscapeLike(variant).ToLowerInvariant()} %";
                }
            }
        }
    }

    private static string DsdSql(TrackFilterColumns columns) =>
        $"(UPPER(COALESCE({columns.Codec}, '')) IN ('DSF', 'DFF', 'DSD') OR COALESCE({columns.SampleRate}, 0) >= 1000000)";

    private static string ExcludeFoldSql(string column) =>
        $"(' ' || LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE({column}, '-', ' '), '_', ' '), '.', ' '), '/', ' '), CHAR(92), ' '), ',', ' ')) || ' ')";

    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    public static string AlbumSearchBlobSql { get; } = """
        (albums.title || ' ' || albums.album_artist || ' ' || COALESCE(CAST(albums.year AS TEXT), '') || ' ' || COALESCE((
          SELECT GROUP_CONCAT(tracks.search_terms || ' ' || tracks.title || ' ' || tracks.artist || ' ' || tracks.album_artist || ' ' || COALESCE(tracks.genre, '') || ' ' || tracks.path, ' ')
          FROM album_tracks
          INNER JOIN tracks ON tracks.id = album_tracks.track_id
          WHERE album_tracks.album_id = albums.id
            AND tracks.missing = 0 AND tracks.library_hidden = 0
        ), ''))
        """;

    public static (string Sql, List<object> Parameters) LikeEntityFilter(
        IReadOnlyList<string> expressions,
        CoreNormalizedEntitySearch? search)
    {
        if (search?.Terms is not { Count: > 0 } terms)
        {
            return (string.Empty, []);
        }

        var clauses = new List<string>();
        var parameters = new List<object>();
        foreach (var term in terms)
        {
            var variants = term.Variants.Where(static variant => !string.IsNullOrWhiteSpace(variant)).ToArray();
            if (variants.Length == 0)
            {
                continue;
            }

            var parts = new List<string>();
            foreach (var variant in variants)
            {
                foreach (var expression in expressions)
                {
                    parts.Add($"{expression} LIKE ? ESCAPE '\\'");
                    parameters.Add($"%{EscapeLike(variant)}%");
                }
            }

            clauses.Add($"({string.Join(" OR ", parts)})");
        }

        return (string.Join(" AND ", clauses), parameters);
    }
}

public readonly record struct TrackFilterColumns(
    string Codec,
    string SampleRate,
    string Path,
    string Year,
    string Id,
    string Title,
    string Artist,
    string Album,
    string AlbumArtist,
    string Genre,
    string FieldSourcesJson)
{
    public static TrackFilterColumns Local { get; } = new(
        "tracks.codec",
        "tracks.sample_rate",
        "tracks.path",
        "tracks.year",
        "tracks.id",
        "tracks.title",
        "tracks.artist",
        "tracks.album",
        "tracks.album_artist",
        "tracks.genre",
        "tracks.field_sources_json");
}
