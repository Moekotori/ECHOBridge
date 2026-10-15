using System.Text.Json;
using Echo.Core.Maintenance;
using Echo.Core.Protocol;

namespace Echo.Core.Playlists;

/// <summary>Manual playlist transactions. Schema initialization remains the application's explicit startup prerequisite.</summary>
public static class PlaylistCommands
{
    public const string Capability = "playlists.commands/v2";
    private static readonly HashSet<string> SortModes = ["manual", "titleAsc", "titleDesc", "artistAsc", "artistDesc", "album", "albumDesc", "artistAlbum", "addedDesc", "addedAsc", "durationAsc", "durationDesc"];
    public static PlaylistCommandResult Execute(string databasePath, PlaylistCommand request)
    {
        if (request.TrackIds is { Count: > 50000 }) throw new ArgumentException("At most 50000 track IDs per command.");
        using var connection = SortKeyMaintenanceSql.OpenWriter(databasePath);
        using var transaction = connection.BeginTransaction();
        var sql = new PlaylistSql(connection, transaction);
        if (request.Operation == "manage" && request.Management?.Action == "state")
            return PlaylistManagementCommands.State(sql);
        if (request.Operation == "manage" && request.Management?.Action == "inspect")
            return PlaylistInspection.Read(sql, request.Management);
        var journal = new Echo.Core.Database.CommandReceiptJournal<PlaylistCommand,PlaylistCommandResult>(sql,
            CoreJsonContext.Default.PlaylistCommand, CoreJsonContext.Default.PlaylistCommandResult, state => new(null, [], state));
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var previous = journal.Find(request, now);
        if (previous is not null) return previous;
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var result = request.Operation switch
        {
            "manage" => PlaylistManagementCommands.Execute(sql, request.Management ?? throw new ArgumentException("Management request required."), timestamp, request.BackupDirectory),
            "inboxCreate" => InboxPlaylistCommand.Execute(sql, request, timestamp),
            "smartCreate" or "smartUpdate" or "smartFreeze" => Echo.Core.SmartPlaylists.SmartPlaylistCommands.Execute(sql, request, timestamp),
            "favorite" => FavoriteCommands.Execute(sql, request, timestamp),
            _ => ExecuteManual(sql, request, timestamp),
        };
        journal.Save(request, result, now);
        transaction.Commit();
        return result;
    }

    private static PlaylistCommandResult ExecuteManual(PlaylistSql sql, PlaylistCommand request, string timestamp)
    {
        var id = request.PlaylistId;
        var itemIds = new List<string>();
        if (request.Operation == "remove") id = sql.Value("SELECT playlist_id FROM playlist_items WHERE id = $0", request.ItemId) as string;
        if (request.Operation != "create" && string.IsNullOrWhiteSpace(id))
        {
            if (request.Operation == "remove") return new(null, []);
            throw new ArgumentException("Playlist ID is required.");
        }
        switch (request.Operation)
        {
            case "create":
                id = Guid.NewGuid().ToString();
                var name = Text(request.Patch, "name")?.Trim();
                if (string.IsNullOrEmpty(name)) throw new ArgumentException("Playlist name is required");
                sql.Run("""
                    INSERT INTO playlists (id,name,description,kind,source_provider,source_playlist_id,cover_id,sort_mode,item_count,created_at,updated_at)
                    VALUES ($0,$1,$2,'manual','local',NULL,NULL,'manual',0,$3,$3)
                    """, id, name, Clean(Text(request.Patch, "description")), timestamp);
                break;
            case "update": Update(sql, id!, request.Patch, timestamp); break;
            case "delete":
                if (Protected(sql, id!)) throw new InvalidOperationException("System playlists cannot be deleted.");
                PlaylistBackupWriter.Write(sql, id!, "delete", request.BackupDirectory, timestamp);
                sql.Run("DELETE FROM playlists WHERE id = $0", id);
                break;
            case "add": itemIds = PlaylistTrackCommands.Add(sql, id!, request.TrackIds ?? [], timestamp); break;
            case "remove":
                PlaylistBackupWriter.Write(sql, id!, "remove-item", request.BackupDirectory, timestamp);
                sql.Run("DELETE FROM playlist_items WHERE id = $0", request.ItemId);
                sql.Resequence(id!); sql.Refresh(id!, timestamp);
                break;
            case "clear":
                PlaylistBackupWriter.Write(sql, id!, "clear", request.BackupDirectory, timestamp);
                sql.Run("DELETE FROM playlist_items WHERE playlist_id = $0", id); sql.Refresh(id!, timestamp);
                break;
            case "move": PlaylistTrackCommands.Move(sql, id!, request.ItemId!, request.TargetPosition, timestamp); break;
            default: throw new ArgumentException("Unknown playlist command.");
        }
        return new(id, itemIds);
    }
    private static bool Protected(PlaylistSql sql, string id) => sql.Value("""
        SELECT 1 FROM playlists WHERE id=$0 AND kind='system' AND source_playlist_id IN ('liked-tracks','liked-albums','daily-recommend')
        """, id) is not null;
    private static string? Text(JsonElement patch, string field) => patch.ValueKind == JsonValueKind.Object && patch.TryGetProperty(field, out var value)
        ? value.ValueKind == JsonValueKind.Null ? null : value.GetString() : null;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Update(PlaylistSql sql, string id, JsonElement patch, string timestamp)
    {
        if (sql.Value("SELECT 1 FROM playlists WHERE id=$0", id) is null) throw new InvalidOperationException("Unknown playlist");
        if (patch.ValueKind != JsonValueKind.Object) throw new ArgumentException("Playlist patch is required.");
        if (Protected(sql, id) && (patch.TryGetProperty("name", out _) || patch.TryGetProperty("description", out _)))
            throw new InvalidOperationException("System playlists cannot be renamed.");
        foreach (var field in new[] { "name", "description", "coverId", "sortMode" })
        {
            if (!patch.TryGetProperty(field, out _)) continue;
            var value = Text(patch, field);
            if (field == "name" && string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Playlist name is required");
            if (field == "sortMode")
            {
                if (value is null) continue;
                if (!SortModes.Contains(value)) throw new ArgumentException("Unsupported playlist sort mode");
            }
            var column = field switch { "coverId" => "cover_id", "sortMode" => "sort_mode", _ => field };
            sql.Run($"UPDATE playlists SET {column}=$0 WHERE id=$1", field is "name" or "description" ? Clean(value) : value, id);
        }
        sql.Run("UPDATE playlists SET updated_at=$0 WHERE id=$1", timestamp, id);
    }
}
