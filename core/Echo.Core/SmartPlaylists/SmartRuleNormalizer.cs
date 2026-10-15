using System.Text.Json.Nodes;

namespace Echo.Core.SmartPlaylists;

internal static class SmartRuleNormalizer
{
    private static readonly HashSet<string> Sorts = ["recentlyAdded", "recentlyPlayed", "mostPlayed", "leastPlayed", "title", "random"];
    public static JsonObject Normalize(JsonObject input)
    {
        var raw = input["rules"] as JsonObject;
        var source = raw?["filters"] as JsonObject ?? new();
        var filters = new JsonObject { ["excludeNeverRecommend"] = SmartRuleValues.Boolean(source["excludeNeverRecommend"]) != false };
        if (SmartRuleValues.Boolean(source["liked"]) is { } liked) filters["liked"] = liked;
        if (SmartRuleValues.Boolean(source["unplayed"]) == true) filters["unplayed"] = true;
        foreach (var key in new[] { "genre", "codec", "folderId" })
            if (SmartRuleValues.Clean(source[key], 160) is { } text) filters[key] = text;
        foreach (var (key, min, max, integer) in new (string, double, double, bool)[] {
            ("yearFrom",1000,9999,false), ("yearTo",1000,9999,false), ("bpmFrom",1,400,false), ("bpmTo",1,400,false),
            ("minPlayCount",0,1_000_000,true), ("maxPlayCount",0,1_000_000,true), ("minSkipCount",0,1_000_000,true),
            ("minSampleRate",1,10_000_000,true), ("addedWithinDays",1,3650,true), ("playedWithinDays",1,3650,true) })
        {
            if (SmartRuleValues.Number(source[key], source.ContainsKey(key)) is { } number)
                filters[key] = Math.Clamp(integer ? Math.Floor(number) : number, min, max);
        }
        if (raw is null)
        {
            filters["minPlayCount"] = 1;
            filters["playedWithinDays"] = SmartRuleValues.Integer(input["recentDays"], 180, 1, 3650, input.ContainsKey("recentDays"));
        }
        var condition = raw?["condition"];
        var budget = 64;
        JsonNode? normalizedCondition = null;
        if (condition is not null)
        {
            normalizedCondition = NormalizeCondition(condition, 0, ref budget);
            if (SmartRuleValues.Text(normalizedCondition["type"]) != "group") throw new ArgumentException("Invalid smart playlist condition");
        }
        var clauses = new JsonArray();
        if (raw?["sorts"] is { } rawSorts)
        {
            if (rawSorts is not JsonArray array || array.Count > 4) throw new ArgumentException("Invalid smart playlist sort clauses");
            foreach (var clause in array)
            {
                if (clause is not JsonObject obj || !SmartRuleSql.SortFields.ContainsKey(SmartRuleValues.Text(obj["field"]) ?? "")
                    || SmartRuleValues.Text(obj["direction"]) is not ("asc" or "desc")) throw new ArgumentException("Invalid smart playlist sort clauses");
                clauses.Add(new JsonObject { ["field"] = SmartRuleValues.Text(obj["field"]), ["direction"] = SmartRuleValues.Text(obj["direction"]) });
            }
        }
        var sort = SmartRuleValues.Text(raw?["sort"]);
        var limit = raw?["limit"] ?? input["limit"];
        var result = new JsonObject {
            ["version"] = normalizedCondition is not null || clauses.Count > 0 ? 2 : 1,
            ["match"] = SmartRuleValues.Text(raw?["match"]) == "any" ? "any" : "all",
            ["limit"] = SmartRuleValues.Integer(limit,100,1,1000,limit is not null || input.ContainsKey("limit")),
            ["sort"] = sort is not null && Sorts.Contains(sort) ? sort : raw is null ? "mostPlayed" : "recentlyAdded",
            ["filters"] = filters,
        };
        if (normalizedCondition is not null) result["condition"] = normalizedCondition;
        if (clauses.Count > 0) result["sorts"] = clauses;
        return result;
    }

    private static JsonObject NormalizeCondition(JsonNode? value, int depth, ref int budget)
    {
        if (value is not JsonObject obj || depth > 3 || budget <= 0) throw new ArgumentException("Invalid smart playlist condition");
        if (SmartRuleValues.Text(obj["type"]) == "rule")
        {
            var field = SmartRuleValues.Text(obj["field"]) ?? "";
            var op = SmartRuleValues.Text(obj["operator"]) ?? "";
            if (!SmartConditionSql.IsValid(field, op)) throw new ArgumentException("Invalid smart playlist condition");
            budget--;
            var result = new JsonObject { ["type"] = "rule", ["field"] = field, ["operator"] = op };
            foreach (var key in new[] { "value", "valueTo" })
            {
                if (!obj.ContainsKey(key)) continue;
                var scalar = obj[key];
                if (scalar is null) result[key] = null;
                else if (SmartRuleValues.Text(scalar) is { } text) result[key] = text.Trim()[..Math.Min(256,text.Trim().Length)];
                else if (SmartRuleValues.Boolean(scalar) is { } flag) { if (key == "value") result[key] = flag; }
                else if (SmartRuleValues.Number(scalar) is { } number) result[key] = number;
            }
            return result;
        }
        if (SmartRuleValues.Text(obj["type"]) != "group" || obj["children"] is not JsonArray children || children.Count == 0)
            throw new ArgumentException("Invalid smart playlist condition");
        var normalized = new JsonArray();
        foreach (var child in children) normalized.Add(NormalizeCondition(child, depth + 1, ref budget));
        return new JsonObject { ["type"] = "group", ["match"] = SmartRuleValues.Text(obj["match"]) == "any" ? "any" : "all", ["children"] = normalized };
    }
}
