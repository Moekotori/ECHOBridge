using Echo.Core.Protocol;

namespace Echo.Core.Playlists;

internal static class PlaylistGroupCommands
{
    internal static List<PlaylistGroup> Read(PlaylistSql sql)
    {
        var groups = new List<PlaylistGroup>();
        using var command = sql.Command("SELECT id,name,collapsed,position FROM playlist_groups ORDER BY position,id LIMIT 100");
        using var reader = command.ExecuteReader();
        while (reader.Read()) groups.Add(new(reader.GetString(0),reader.GetString(1),reader.GetInt32(2)!=0,reader.GetInt32(3)));
        return groups;
    }

    internal static PlaylistCommandResult Execute(PlaylistSql sql, PlaylistManagementRequest request)
    {
        var id = request.GroupId;
        if (request.Action == "group-create")
        {
            if (Convert.ToInt32(sql.Value("SELECT COUNT(*) FROM playlist_groups")) >= 100)
                throw new InvalidOperationException("At most 100 playlist groups.");
            id = Guid.NewGuid().ToString();
            sql.Run("INSERT INTO playlist_groups(id,name,position) VALUES ($0,$1,(SELECT COALESCE(MAX(position),-1)+1 FROM playlist_groups))", id, Name(request.Name));
        }
        else
        {
            if (id is not null && sql.Value("SELECT 1 FROM playlist_groups WHERE id=$0", id) is null)
                throw new InvalidOperationException("Group changed. Refresh the playlists.");
            switch (request.Action)
            {
                case "group-assign":
                    PlaylistManagementCommands.RequireEditable(sql,request.PlaylistId ?? "",true);
                    if (id is null) sql.Run("DELETE FROM playlist_group_members WHERE playlist_id=$0",request.PlaylistId);
                    else sql.Run("INSERT INTO playlist_group_members(playlist_id,group_id) VALUES ($0,$1) ON CONFLICT(playlist_id) DO UPDATE SET group_id=excluded.group_id",request.PlaylistId,id);
                    break;
                case "group-update":
                    if (id is null || (request.Name is null && request.Collapsed is null)) throw new ArgumentException("Group update required.");
                    if (request.Name is not null) sql.Run("UPDATE playlist_groups SET name=$0 WHERE id=$1",Name(request.Name),id);
                    if (request.Collapsed is not null) sql.Run("UPDATE playlist_groups SET collapsed=$0 WHERE id=$1",request.Collapsed.Value ? 1 : 0,id);
                    break;
                case "group-delete":
                    if (id is null) throw new ArgumentException("Group required.");
                    sql.Run("DELETE FROM playlist_group_members WHERE group_id=$0",id);
                    sql.Run("DELETE FROM playlist_groups WHERE id=$0",id);
                    break;
                default: throw new ArgumentException("Unknown group action.");
            }
        }
        return new(request.PlaylistId,[],Groups:Read(sql));
    }

    private static string Name(string? name)
    {
        var text = name?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 256) throw new ArgumentException("Group name must contain 1 to 256 characters.");
        return text;
    }
}
