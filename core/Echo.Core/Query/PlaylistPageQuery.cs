using Echo.Core.Database;
using Echo.Core.Protocol;

namespace Echo.Core.Query;

/// <summary>Read-only configuration pages; select and enrich only the requested playlists.</summary>
public static class PlaylistPageQuery
{
    public static LibraryPageDto<LibraryPlaylistDto> Execute(ReadOnlyLibraryDatabase database, CoreLibraryPageQuery query)
    {
        var order = query.Sort switch
        {
            null or "default" or "title" or "titleAsc" => "name COLLATE NOCASE ASC, id",
            "titleDesc" => "name COLLATE NOCASE DESC, id",
            "recent" => "updated_at DESC, name COLLATE NOCASE, id",
            "createdAsc" or "addedAsc" => "created_at ASC, id",
            "createdDesc" => "created_at DESC, id",
            _ => throw new UnsupportedQueryException("Unsupported playlist page sort."),
        };
        var page = PageQueryNormalizer.Normalize(query);
        var connection = database.RequireConnection();
        connection.CreateFunction("echo_playlist_name_contains", static (string name, string search) =>
            name.Contains(search, StringComparison.OrdinalIgnoreCase), isDeterministic: true);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA temp_store=FILE";
        command.ExecuteNonQuery();
        using var transaction = database.BeginDeferredRead();
        var where = PlaylistListQuery.Visible + " AND echo_playlist_name_contains(name,$search)";
        var search = query.Search?.Trim() ?? "";
        command.Parameters.AddWithValue("$search", search);
        command.CommandText = $"SELECT COUNT(*) FROM playlists WHERE {where}";
        var total = (long)command.ExecuteScalar()!;
        command.Parameters.AddWithValue("$limit", page.PageSize);
        command.Parameters.AddWithValue("$offset", page.Offset);
        command.CommandText = $"SELECT {PlaylistListQuery.Columns} FROM playlists WHERE {where} ORDER BY {order} LIMIT $limit OFFSET $offset";
        var items = new List<LibraryPlaylistDto>(page.PageSize);
        using (var rows = command.ExecuteReader())
            while (rows.Read()) items.Add(PlaylistListQuery.Map(rows));
        PlaylistListQuery.Enrich(database, items);
        return new() { Items=items, Total=total, Page=page.Page, PageSize=page.PageSize, HasMore=page.Offset+items.Count<total };
    }
}
