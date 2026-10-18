using System.Text;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Echo.Core.Search;

namespace Echo.Core.Query;

public static class GlobalCatalogQuery
{
    private const string Playlists = "NOT (kind = 'system' AND source_provider = 'local' AND source_playlist_id IN ('liked-tracks', 'liked-albums'))";
    private const string Folders = "enabled = 1 AND status = 'active'";
    public static CatalogSourceDto Execute(ReadOnlyLibraryDatabase database, CoreCatalogQuery input, CoreSearchOptions? options)
    {
        var query = input.Search.Normalize(NormalizationForm.FormKC).Trim();
        query = query[..Math.Min(query.Length, 120)];
        var limit = Math.Clamp(input.Limit == 0 ? 12 : input.Limit, 1, 24);
        using var transaction = database.BeginDeferredRead();
        if (input.Kind is "playlists" or "folders")
        {
            if (input.Cursor < 0) throw new ArgumentException("Invalid catalog cursor.");
            var rows = new List<CatalogCandidateDto>();
            var table = input.Kind;
            using var reader = CachedQuery.Reader(database,
                $"SELECT rowid,id,name FROM {table} WHERE {(table == "playlists" ? Playlists : Folders)} AND rowid > ? ORDER BY rowid LIMIT 128",
                command => command.Add(input.Cursor));
            while (reader.Read()) rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            return new([], [], rows);
        }
        var playlists = new List<CatalogPlaylistDto>();
        var folders = new List<CatalogFolderDto>();
        var search = LibrarySearchNormalizer.Entity(query, options?.ChineseCrossScriptSearchEnabled != false);
        if (search.Terms.Count == 0) return new(playlists, folders);
        foreach (var table in new[] { "playlists", "folders" })
        {
            var filter = SqlFragments.LikeEntityFilter(table == "playlists" ? ["name", "COALESCE(description, '')"] : ["name", "path"], search);
            using var reader = CachedQuery.Reader(database, $"""
                SELECT id,name,{(table == "playlists" ? "description,item_count,cover_id,cover_url" : "path")}
                FROM {table} WHERE {(table == "playlists" ? Playlists : Folders)} AND {filter.Sql}
                ORDER BY CASE WHEN name = ? COLLATE NOCASE THEN 0 WHEN name LIKE ? ESCAPE '\' THEN 1 ELSE 2 END,
                  name COLLATE NOCASE,id LIMIT ?
                """, command =>
                {
                    foreach (var value in filter.Parameters) command.Add(value);
                    command.Add(query); command.Add(SqlFragments.EscapeLike(query) + "%"); command.Add(limit);
                });
            while (reader.Read())
            {
                string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                if (table == "playlists") playlists.Add(new(reader.GetString(0), reader.GetString(1), Text(2), reader.GetInt32(3), Text(4), Text(5)));
                else folders.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }
        return new(playlists, folders);
    }

    public static CatalogSourceDto Hydrate(ReadOnlyLibraryDatabase database, string kind, List<string> ids)
    {
        if (ids.Count > 24) throw new ArgumentException("Catalog hydration exceeds its result bound.");
        var playlists = new List<CatalogPlaylistDto>(); var folders = new List<CatalogFolderDto>();
        var table = kind == "playlists" ? "playlists" : "folders";
        using var reader = CachedQuery.Reader(database,
            $"SELECT id,name,{(table == "playlists" ? "description,item_count,cover_id,cover_url" : "path")} FROM {table} WHERE id {SqlFragments.JsonEachIds}",
            command => command.Add(System.Text.Json.JsonSerializer.Serialize(ids)));
        while (reader.Read())
        {
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            if (table == "playlists") playlists.Add(new(reader.GetString(0), reader.GetString(1), Text(2), reader.GetInt32(3), Text(4), Text(5)));
            else folders.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return new(playlists, folders);
    }
}
