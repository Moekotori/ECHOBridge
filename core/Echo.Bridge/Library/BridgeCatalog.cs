using Echo.Core.Protocol;

namespace Echo.Bridge.Library;

public sealed class BridgeCatalog(BridgeLibraryReader reader)
{
    private readonly BridgeFolderReferences folders = new();
    public void Clear() => folders.Clear();

    public object Page(string kind, int page, string search, string? sort, string? parentKind, string? parentId, CancellationToken cancellation)
    {
        if (page is < 1 or > 100_000 || search.Length > 256 || parentId?.Length > 2048 || sort == "random")
            throw new BridgeException(400, "invalid_peer_catalog_query");
        if (parentKind is not null && kind is "artists" or "genres" or "playlists" or "liked")
            throw new BridgeException(400, "invalid_peer_catalog_parent");
        var query = new CoreLibraryPageQuery { Page = page, PageSize = 50, Search = search, Sort = sort ?? "titleAsc", SourceProvider = "local" };
        var parameters = new CoreRequestParams { Query = query };
        List<Dictionary<string, object?>> entries;
        long total;
        if (kind is "songs" or "liked")
        {
            if (kind == "liked" || parentKind == "playlist")
            {
                parameters.PlaylistId = kind == "liked" ? BridgeLibraryMetadata.FavoritesId(reader.Profile, cancellation) : Required(parentId);
                if (parameters.PlaylistId is null) { entries = []; total = 0; }
                else
                {
                    var result = (LibraryPageDto<LibraryPlaylistItemDto>)reader.Read(kind == "liked" ? "getFavoriteItems" : "getPlaylistItems", parameters, cancellation)!;
                    entries = result.Items.Select(BridgeTrackProjection.PlaylistEntry).ToList(); total = result.Total;
                }
            }
            else
            {
                var method = parentKind switch
                {
                    null => "getTracks", "album" => "getAlbumTracks", "artist" => "getArtistTracks",
                    "genre" => "getGenreTracks", "folder" => "getFolderTracks", _ => throw new BridgeException(400, "invalid_peer_catalog_parent"),
                };
                if (parentKind is not null) Required(parentId);
                parameters.AlbumId = parentKind == "album" ? parentId : null;
                parameters.ArtistId = parentKind == "artist" ? parentId : null;
                parameters.GenreKey = parentKind == "genre" ? parentId : null;
                if (parentKind == "folder")
                {
                    var folder = folders.Resolve(parentId!); query.FolderId = folder.FolderId; query.Path = folder.Path; query.Recursive = true;
                }
                var result = (LibraryPageDto<LibraryTrackListRowDto>)reader.Read(method, parameters, cancellation)!;
                entries = result.Items.Select(BridgeTrackProjection.Entry).ToList(); total = result.Total;
            }
        }
        else if (kind == "albums")
        {
            if (parentKind is not (null or "artist")) throw new BridgeException(400, "invalid_peer_catalog_parent");
            parameters.ArtistId = parentKind is null ? null : Required(parentId);
            var result = (LibraryPageDto<LibraryAlbumDto>)reader.Read(parentKind is null ? "getAlbums" : "getArtistAlbums", parameters, cancellation)!;
            entries = result.Items.Select(row => BridgeTrackProjection.Container("album", row.Id, row.Title, row.AlbumArtist, row.TrackCount, row.CoverId)).ToList(); total = result.Total;
        }
        else if (kind == "artists")
        {
            var result = (LibraryPageDto<LibraryArtistDto>)reader.Read("getArtists", parameters, cancellation)!;
            entries = result.Items.Select(row => BridgeTrackProjection.Container("artist", row.Id, row.Name, "", row.TrackCount, row.CoverId)).ToList(); total = result.Total;
        }
        else if (kind == "genres")
        {
            var result = (LibraryPageDto<LibraryGenreDto>)reader.Read("getGenres", parameters, cancellation)!;
            entries = result.Items.Select(row => BridgeTrackProjection.Container("genre", row.GenreKey, row.Name, "", row.TrackCount, null)).ToList(); total = result.Total;
        }
        else if (kind == "playlists")
        {
            var rows = (IEnumerable<LibraryPlaylistDto>)reader.Read("getPlaylists", cancellation: cancellation)!;
            (entries, total) = ConfigurationPage(rows, search, page, row => row.Name,
                row => BridgeTrackProjection.Container("playlist", row.Id, row.Name, row.Description ?? "", row.ItemCount, row.CoverId));
        }
        else if (kind == "folders" && parentKind == "folder")
        {
            var folder = folders.Resolve(Required(parentId));
            parameters.FolderChildren = new() { FolderId = folder.FolderId, ParentPath = folder.Path };
            var rows = (IEnumerable<LibraryFolderNodeDto>)reader.Read("getFolderChildren", parameters, cancellation)!;
            (entries, total) = ConfigurationPage(rows, search, page, row => row.Name,
                row => BridgeTrackProjection.Container("folder", folders.Remember(row), row.Name, "", row.TrackCount, null));
        }
        else if (kind == "folders" && parentKind is null)
            (entries, total) = BridgeLibraryMetadata.Folders(reader.Profile, search, page, cancellation);
        else throw new BridgeException(400, "invalid_peer_catalog_query");

        var response = new Dictionary<string, object?> { ["entries"] = entries, ["total"] = total, ["page"] = page, ["pageSize"] = 50, ["supportsSorting"] = true };
        if (kind == "albums" && parentKind == "artist") response["parent"] = new { kind = parentKind, id = parentId };
        return response;
    }

    private static string Required(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 2048
        ? value : throw new BridgeException(400, "invalid_peer_catalog_parent");

    private static (List<Dictionary<string, object?>> Entries, long Total) ConfigurationPage<T>(IEnumerable<T> rows,
        string search, int page, Func<T, string> name, Func<T, Dictionary<string, object?>> project)
    {
        var entries = new List<Dictionary<string, object?>>(50);
        long total = 0, offset = (page - 1L) * 50;
        foreach (var row in rows)
        {
            if (!name(row).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if (total >= offset && entries.Count < 50) entries.Add(project(row));
            total++;
        }
        return (entries, total);
    }
}
