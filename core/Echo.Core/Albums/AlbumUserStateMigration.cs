using System.Text.Json.Nodes;
using Echo.Core.Database;

namespace Echo.Core.Albums;

/// <summary>Copies explicit choices to every reconciled target, retaining donor profiles for conflicts.</summary>
internal static class AlbumUserStateMigration
{
    private static readonly string[] IdentityColumns = ["provider", "provider_item_id", "release_group_id", "identity_state",
        "online_values_json", "source_snapshot_json", "track_mapping_json", "identified_at"];

    public static void Apply(TransactionSql sql, CancellationToken cancellation)
    {
        AlbumVersionIndexMigration.Apply(sql, cancellation);
        using var read = sql.Command("SELECT old_id,id FROM ag_targets WHERE old_id<>id ORDER BY old_id,count DESC,id");
        using var reader = read.ExecuteReader();
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            var oldId = reader.GetString(0);
            var id = reader.GetString(1);
            sql.Run("""
                UPDATE albums SET user_rating=(SELECT user_rating FROM ag_old WHERE id=$0 AND user_rating BETWEEN 1 AND 5)
                WHERE id=$1 AND user_rating IS NULL;
                INSERT OR IGNORE INTO personal_tag_items(tag_id,kind,target_id,title,subtitle)
                  SELECT tag_id,kind,$1,title,subtitle FROM personal_tag_items WHERE kind='album' AND target_id=$0;
                INSERT OR IGNORE INTO listen_later_albums
                  SELECT $1,title,artist,added_at,completed,track_id,position_seconds,track_finished
                  FROM listen_later_albums WHERE album_id=$0;
                """, oldId, id);
            var donor = Profile(sql, oldId);
            if (donor is null) continue;
            var existing = Profile(sql, id);
            var identity = existing?.GetValueOrDefault("identity_state") as string == "pinned" ? existing
                : donor.GetValueOrDefault("identity_state") as string == "pinned" ? donor : existing ?? donor;
            var user = Object(donor.GetValueOrDefault("user_values_json") as string);
            foreach (var pair in Object(existing?.GetValueOrDefault("user_values_json") as string)) user[pair.Key] = pair.Value?.DeepClone();
            var updatedAt = StringComparer.Ordinal.Compare(existing?.GetValueOrDefault("updated_at") as string ?? "",
                donor.GetValueOrDefault("updated_at") as string ?? "") > 0 ? existing!["updated_at"] : donor["updated_at"];
            var values = new List<object?> { id };
            values.AddRange(IdentityColumns.Select(column => identity!.GetValueOrDefault(column)));
            values.Add(user.ToJsonString());
            values.Add(existing?.GetValueOrDefault("created_at") ?? donor["created_at"]);
            values.Add(updatedAt);
            sql.Run("""
                INSERT INTO album_metadata_profiles(album_id,provider,provider_item_id,release_group_id,identity_state,
                  online_values_json,source_snapshot_json,track_mapping_json,identified_at,user_values_json,created_at,updated_at)
                VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
                ON CONFLICT(album_id) DO UPDATE SET provider=excluded.provider,provider_item_id=excluded.provider_item_id,
                  release_group_id=excluded.release_group_id,identity_state=excluded.identity_state,
                  online_values_json=excluded.online_values_json,source_snapshot_json=excluded.source_snapshot_json,
                  track_mapping_json=excluded.track_mapping_json,identified_at=excluded.identified_at,
                  user_values_json=excluded.user_values_json,updated_at=excluded.updated_at
                """, values.ToArray());
        }
    }

    private static Dictionary<string, object?>? Profile(TransactionSql sql, string id)
    {
        using var command = sql.Command("SELECT * FROM album_metadata_profiles WHERE album_id=$0", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++) values.Add(reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i));
        return values;
    }
    private static JsonObject Object(string? json)
    {
        try { return JsonNode.Parse(json ?? "{}") as JsonObject ?? new(); }
        catch (System.Text.Json.JsonException) { return new(); }
    }
}
