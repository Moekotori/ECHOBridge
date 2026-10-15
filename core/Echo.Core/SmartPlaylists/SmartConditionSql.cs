using System.Text.Json.Nodes;

namespace Echo.Core.SmartPlaylists;

internal static class SmartConditionSql
{
    private static readonly Dictionary<string, (string Sql, string Kind)> Fields = new() {
        ["title"]=("tracks.title","text"), ["artist"]=("tracks.artist","text"), ["album"]=("tracks.album","text"),
        ["albumArtist"]=("tracks.album_artist","text"), ["genre"]=("tracks.genre","text"), ["codec"]=("tracks.codec","text"),
        ["path"]=("tracks.path","text"), ["replayGainStatus"]=("tracks.replay_gain_status","text"),
        ["year"]=("tracks.year","number"), ["bpm"]=("tracks.bpm","number"), ["duration"]=("tracks.duration","number"),
        ["playCount"]=("COALESCE(tracks.play_count,0)","number"), ["skipCount"]=("COALESCE(tracks.skip_count,0)","number"),
        ["sampleRate"]=("tracks.sample_rate","number"), ["bitDepth"]=("tracks.bit_depth","number"), ["bitrate"]=("tracks.bitrate","number"),
        ["dateAdded"]=("tracks.created_at","date"), ["lastPlayed"]=("tracks.last_played_at","date"), ["lastSkipped"]=("tracks.last_skipped_at","date"),
        ["liked"]=("","liked"), ["instrumental"]=("","instrumental"),
    };
    public static bool IsValid(string field, string op) => Fields.TryGetValue(field, out var entry) && entry.Kind switch {
        "text" => op is "is" or "isNot" or "contains" or "notContains" or "startsWith" or "endsWith" or "isEmpty" or "isNotEmpty",
        "date" => op is "withinLastDays" or "notWithinLastDays" or "isEmpty" or "isNotEmpty",
        "liked" or "instrumental" => op is "is" or "isNot",
        _ => op is "is" or "isNot" or "greaterThan" or "atLeast" or "lessThan" or "atMost" or "between" or "isEmpty" or "isNotEmpty",
    };
    public static string Liked(bool liked) => $"""
        {(liked ? "" : "NOT ")}EXISTS (SELECT 1 FROM playlist_items AS li INNER JOIN playlists AS lp ON lp.id=li.playlist_id
          WHERE lp.kind='system' AND lp.source_provider='local' AND lp.source_playlist_id='liked-tracks'
            AND li.media_type IN ('track','stream_track') AND li.media_id=tracks.id)
        """;
    public static string? Compile(JsonObject node, List<object?> parameters, DateTimeOffset now)
    {
        if (SmartRuleValues.Text(node["type"]) == "group")
        {
            var children = node["children"]!.AsArray().Select(child => Compile(child!.AsObject(), parameters, now))
                .Where(sql => sql is not null).Select(sql => $"({sql})").ToList();
            return children.Count == 0 ? null : "(" + string.Join(SmartRuleValues.Text(node["match"]) == "any" ? " OR " : " AND ", children) + ")";
        }
        var (column, kind) = Fields[SmartRuleValues.Text(node["field"])!];
        var op = SmartRuleValues.Text(node["operator"]);
        var value = node["value"];
        string Bind(object? v) { var name = "$" + parameters.Count; parameters.Add(v); return name; }
        if (kind is "liked" or "instrumental")
        {
            var expected = SmartRuleValues.Boolean(value) == true || SmartRuleValues.Text(value) == "true"
                || SmartRuleValues.Text(value) is null && SmartRuleValues.Number(value) == 1;
            var wanted = op == "isNot" ? !expected : expected;
            if (kind == "liked") return Liked(wanted);
            return $"{Query.Sql.VocalPresenceSql.Expression("tracks.id", "tracks.title", "tracks.album")} {(wanted ? "=" : "IS NOT")} 'instrumental'";
        }
        if (op is "isEmpty" or "isNotEmpty")
        {
            var empty = kind == "text" ? $"TRIM(COALESCE({column},''))=''" : $"{column} IS NULL";
            return op == "isNotEmpty" ? $"NOT ({empty})" : empty;
        }
        if (kind == "date")
        {
            var days = SmartRuleValues.Integer(value,0,1,3650,node.ContainsKey("value"));
            if (days <= 0) return null;
            var parameter = Bind(SmartRuleValues.Threshold(days,now));
            return op == "withinLastDays" ? $"{column}>={parameter}" : $"({column} IS NULL OR {column}<{parameter})";
        }
        if (kind == "text")
        {
            var text = SmartRuleValues.Text(value)?.Trim() ?? value?.ToJsonString().Trim() ?? "";
            if (text.Length == 0) return null;
            if (op is "is" or "isNot") return $"{(op == "isNot" ? "NOT " : "")}(LOWER(COALESCE({column},''))=LOWER({Bind(text)}))";
            var escaped = text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = op == "startsWith" ? escaped + "%" : op == "endsWith" ? "%" + escaped : "%" + escaped + "%";
            return $"{(op == "notContains" ? "NOT " : "")}(COALESCE({column},'') LIKE {Bind(pattern)} ESCAPE '\\' COLLATE NOCASE)";
        }
        if (SmartRuleValues.Number(value,node.ContainsKey("value")) is not { } number) return null;
        if (op == "between")
        {
            if (SmartRuleValues.Number(node["valueTo"],node.ContainsKey("valueTo")) is not { } upper) return null;
            return $"{column} BETWEEN {Bind(Math.Min(number,upper))} AND {Bind(Math.Max(number,upper))}";
        }
        var comparison = op switch { "is" => "=", "isNot" => "!=", "greaterThan" => ">", "atLeast" => ">=", "lessThan" => "<", "atMost" => "<=", _ => throw new ArgumentException("Invalid comparison") };
        return $"{column}{comparison}{Bind(number)}";
    }
}
