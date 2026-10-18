using System.Text.Json.Nodes;
using Echo.Core.Playlists;

namespace Echo.Core.SmartPlaylists;

internal static class SmartPlaylistOrder
{
    // Keep rule membership/limit authoritative. Sorting only applies to those members.
    public static string SelectIds(SmartQuery query, JsonObject rules, int count, int offset, List<object?> parameters)
    {
        if (rules["manualOrder"] is not JsonArray order)
            return $"SELECT tracks.id FROM tracks WHERE {query.Where} ORDER BY {query.Order} LIMIT {count} OFFSET {offset}";
        var orderParameter = "$" + parameters.Count;
        parameters.Add(order.ToJsonString());
        return $"""
            WITH members AS MATERIALIZED (
                SELECT tracks.id FROM tracks WHERE {query.Where} ORDER BY {query.Order} LIMIT {rules["limit"]}
            ), ranked AS (
                SELECT tracks.id, ROW_NUMBER() OVER (ORDER BY {query.Order}) AS rule_position
                FROM members INNER JOIN tracks ON tracks.id = members.id
            )
            SELECT ranked.id FROM ranked LEFT JOIN json_each({orderParameter}) AS saved ON saved.value = ranked.id
            ORDER BY saved.key IS NULL, CAST(saved.key AS INTEGER), ranked.rule_position LIMIT {count} OFFSET {offset}
            """;
    }

    public static JsonObject Normalize(JsonObject stored)
    {
        var rules = SmartRuleNormalizer.Normalize(new JsonObject { ["rules"] = stored.DeepClone() });
        if (stored["manualOrder"] is JsonArray order) rules["manualOrder"] = order.DeepClone();
        return rules;
    }

    public static void Move(PlaylistSql sql, string id, string itemId, int target, string timestamp)
    {
        var json = sql.Value("SELECT rules_json FROM smart_playlist_rules WHERE playlist_id=$0", id) as string
            ?? throw new InvalidOperationException("Missing smart playlist rules");
        var stored = JsonNode.Parse(json)!.AsObject();
        var rules = Normalize(stored);
        var query = SmartRuleSql.Build(rules, DateTimeOffset.Parse(timestamp));
        var parameters = new List<object?>(query.Parameters);
        var ids = new List<string>();
        using (var command = sql.Command(SelectIds(query, rules, (int)SmartRuleValues.Number(rules["limit"])!, 0, parameters), parameters.ToArray()))
        using (var reader = command.ExecuteReader()) { while (reader.Read()) ids.Add(reader.GetString(0)); }
        var from = ids.FindIndex(trackId => $"smart:{id}:{trackId}" == itemId);
        if (from < 0) throw new InvalidOperationException("Unknown smart playlist item");
        var moved = ids[from];
        ids.RemoveAt(from);
        ids.Insert(Math.Clamp(target, 0, ids.Count), moved);
        stored["manualOrder"] = new JsonArray(ids.Select(trackId => (JsonNode?)JsonValue.Create(trackId)).ToArray());
        sql.Run("UPDATE smart_playlist_rules SET rules_json=$0,updated_at=$1 WHERE playlist_id=$2", stored.ToJsonString(), timestamp, id);
        sql.Run("UPDATE playlists SET updated_at=$0 WHERE id=$1", timestamp, id);
    }
}
