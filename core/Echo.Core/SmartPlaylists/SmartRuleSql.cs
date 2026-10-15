using System.Text.Json.Nodes;

namespace Echo.Core.SmartPlaylists;

internal sealed record SmartQuery(string Where, string Order, List<object?> Parameters);

internal static class SmartRuleSql
{
    internal static readonly Dictionary<string,string> SortFields = new() {
        ["dateAdded"]="tracks.created_at", ["lastPlayed"]="tracks.last_played_at", ["playCount"]="COALESCE(tracks.play_count,0)",
        ["skipCount"]="COALESCE(tracks.skip_count,0)", ["title"]="tracks.title COLLATE NOCASE", ["artist"]="tracks.artist COLLATE NOCASE",
        ["album"]="tracks.album COLLATE NOCASE", ["year"]="tracks.year", ["bpm"]="tracks.bpm", ["duration"]="tracks.duration", ["bitrate"]="tracks.bitrate",
    };
    public static SmartQuery Build(JsonObject rules, DateTimeOffset now, string? search = null)
    {
        var parameters = new List<object?>();
        string Bind(object? value) { var name = "$" + parameters.Count; parameters.Add(value); return name; }
        var filters = new List<string>();
        var value = rules["filters"]!.AsObject();
        if (SmartRuleValues.Boolean(value["liked"]) is { } liked) filters.Add(SmartConditionSql.Liked(liked));
        if (SmartRuleValues.Boolean(value["unplayed"]) == true) filters.Add("COALESCE(tracks.play_count,0)=0");
        foreach (var key in new[] { "genre", "codec" })
            if (SmartRuleValues.Text(value[key]) is { } text) filters.Add($"LOWER(COALESCE(tracks.{key},''))=LOWER({Bind(text)})");
        if (SmartRuleValues.Text(value["folderId"]) is { } folder) filters.Add($"tracks.folder_id={Bind(folder)}");
        foreach (var (from,to,column) in new[] { ("yearFrom","yearTo","tracks.year"), ("bpmFrom","bpmTo","tracks.bpm"), ("minPlayCount","maxPlayCount","COALESCE(tracks.play_count,0)") })
        {
            var range = new List<string>();
            if (value.ContainsKey(from)) range.Add($"{column}>={Bind(SmartRuleValues.Number(value[from]))}");
            if (value.ContainsKey(to)) range.Add($"{column}<={Bind(SmartRuleValues.Number(value[to]))}");
            if (range.Count > 0) filters.Add(string.Join(" AND ",range));
        }
        foreach (var (key,column) in new[] { ("minSkipCount","COALESCE(tracks.skip_count,0)"), ("minSampleRate","COALESCE(tracks.sample_rate,0)") })
            if (value.ContainsKey(key)) filters.Add($"{column}>={Bind(SmartRuleValues.Number(value[key]))}");
        foreach (var (key,column) in new[] { ("addedWithinDays","tracks.created_at"), ("playedWithinDays","tracks.last_played_at") })
            if (SmartRuleValues.Number(value[key],value.ContainsKey(key)) is { } days) filters.Add($"{column}>={Bind(SmartRuleValues.Threshold(days,now))}");
        var clauses = new List<string> { "tracks.missing=0 AND tracks.library_hidden=0" };
        if (SmartRuleValues.Boolean(value["excludeNeverRecommend"]) != false) clauses.Add("COALESCE(tracks.recommendation_preference,'neutral')!='never_recommend'");
        if (filters.Count > 0) clauses.Add("(" + string.Join(SmartRuleValues.Text(rules["match"]) == "any" ? " OR " : " AND ",filters.Select(f => $"({f})")) + ")");
        if (rules["condition"] is JsonObject condition && SmartConditionSql.Compile(condition,parameters,now) is { } compiled) clauses.Add(compiled);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var parameter = Bind("%" + search.Trim() + "%");
            clauses.Add("(" + string.Join(" OR ",new[] { "title","artist","album","genre","path" }.Select(c => $"tracks.{c} LIKE {parameter} ESCAPE '\\'")) + ")");
        }
        var order = SmartRuleValues.Text(rules["sort"]) switch {
            "recentlyPlayed" => "CASE WHEN tracks.last_played_at IS NULL THEN 1 ELSE 0 END,tracks.last_played_at DESC,tracks.title COLLATE NOCASE",
            "mostPlayed" => "COALESCE(tracks.play_count,0) DESC,tracks.last_played_at DESC,tracks.title COLLATE NOCASE",
            "leastPlayed" => "COALESCE(tracks.play_count,0) ASC,tracks.created_at DESC,tracks.title COLLATE NOCASE",
            "title" => "tracks.title COLLATE NOCASE,tracks.artist COLLATE NOCASE,tracks.id",
            "random" => "tracks.id",
            _ => "tracks.created_at DESC,tracks.title COLLATE NOCASE,tracks.id",
        };
        if (rules["sorts"] is JsonArray { Count: > 0 } sorts)
            order = string.Join(',',sorts.Select(s => SortFields[SmartRuleValues.Text(s!["field"])!] + " " + SmartRuleValues.Text(s["direction"])!.ToUpperInvariant())) + ",tracks.id";
        return new(string.Join(" AND ",clauses),order,parameters);
    }
}
