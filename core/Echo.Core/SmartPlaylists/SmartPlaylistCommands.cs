using System.Text.Json.Nodes;
using Echo.Core.Playlists;
using Echo.Core.Protocol;

namespace Echo.Core.SmartPlaylists;

internal static class SmartPlaylistCommands
{
    public static PlaylistCommandResult Execute(PlaylistSql sql, PlaylistCommand request, string timestamp)
    {
        var input = SmartRuleValues.Object(request.SmartInput);
        var id = request.PlaylistId;
        string? currentName = null, description = null;
        JsonObject? currentRules = null;
        if (request.Operation != "smartCreate")
        {
            if (string.IsNullOrEmpty(id) || sql.Value("SELECT kind FROM playlists WHERE id=$0",id) as string != "smart")
                throw new ArgumentException("Unknown smart playlist");
            currentName = sql.Value("SELECT name FROM playlists WHERE id=$0",id) as string;
            description = sql.Value("SELECT description FROM playlists WHERE id=$0",id) as string;
            var json = sql.Value("SELECT rules_json FROM smart_playlist_rules WHERE playlist_id=$0",id) as string;
            currentRules = json is null ? null : JsonNode.Parse(json)?.AsObject();
        }
        if (request.Operation == "smartFreeze")
        {
            if (currentRules is null) throw new ArgumentException("Missing smart playlist rules");
            var rules = SmartPlaylistOrder.Normalize(currentRules);
            var query = SmartRuleSql.Build(rules, DateTimeOffset.Parse(timestamp));
            var trackIds = new List<string>();
            var parameters = new List<object?>(query.Parameters);
            var selectIds = SmartPlaylistOrder.SelectIds(query, rules, (int)SmartRuleValues.Number(rules["limit"])!, 0, parameters);
            using (var command = sql.Command(selectIds,parameters.ToArray()))
            using (var reader = command.ExecuteReader()) { while (reader.Read()) trackIds.Add(reader.GetString(0)); }
            id = Guid.NewGuid().ToString();
            Insert(sql,id,SmartRuleValues.Clean(input["name"]) ?? currentName + "（固定）",description,"manual",timestamp);
            var items = PlaylistTrackCommands.Add(sql,id,trackIds,timestamp);
            return new(id,items);
        }
        if (request.Operation == "smartUpdate" && input["rules"] is null && currentRules is not null) input["rules"] = currentRules;
        var normalized = SmartRuleNormalizer.Normalize(input);
        if (currentRules?["manualOrder"] is JsonArray order) normalized["manualOrder"] = order.DeepClone();
        if (request.Operation == "smartCreate")
        {
            id = Guid.NewGuid().ToString();
            Insert(sql,id,SmartRuleValues.Clean(input["name"]) ?? "动态歌单 " + timestamp[..10],
                SmartRuleValues.Clean(input["description"]) ?? "根据曲库规则自动更新。","smart",timestamp);
        }
        else if (request.Operation == "smartUpdate")
            sql.Run("UPDATE playlists SET name=$0,description=$1,updated_at=$2 WHERE id=$3",
                SmartRuleValues.Clean(input["name"]) ?? currentName,
                input.ContainsKey("description") ? SmartRuleValues.Clean(input["description"]) : description,timestamp,id);
        else throw new ArgumentException("Unknown smart command");
        sql.Run("""
            INSERT INTO smart_playlist_rules(playlist_id,version,rules_json,created_at,updated_at) VALUES($0,$1,$2,$3,$3)
            ON CONFLICT(playlist_id) DO UPDATE SET version=excluded.version,rules_json=excluded.rules_json,updated_at=excluded.updated_at
            """,id,SmartRuleValues.Number(normalized["version"]),normalized.ToJsonString(),timestamp);
        return new(id,[]);
    }
    private static void Insert(PlaylistSql sql,string id,string name,string? description,string kind,string timestamp) => sql.Run("""
        INSERT INTO playlists(id,name,description,kind,source_provider,sort_mode,item_count,created_at,updated_at)
        VALUES($0,$1,$2,$3,'local','manual',0,$4,$4)
        """,id,name,description,kind,timestamp);
}
