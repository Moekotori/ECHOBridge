using System.Text;
using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

public static class AlbumArtistQueries
{
    public static string? LocalKey(string? id)
    {
        if (id?.StartsWith("album-artist:", StringComparison.Ordinal) != true) return null;
        var hex = id[13..];
        if (hex.Length == 0 || hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit)) return null;
        var bytes = Convert.FromHexString(hex);
        var text = Encoding.UTF8.GetString(bytes);
        if (!Encoding.UTF8.GetBytes(text).SequenceEqual(bytes)) return null;
        var parts = text.Split('\u001f');
        return parts.Length == 2 && parts[0] == "l" && parts[1].Length > 0 ? parts[1] : null;
    }

    public static LibraryPageDto<LibraryArtistDto> Artists(ReadOnlyLibraryDatabase database, CoreLibraryPageQuery? query, CoreSearchOptions? options)
    {
        if (!QueryCapabilities.SupportsArtists(query, Echo.Core.Search.LibrarySearchNormalizer.Entity(query?.Search)))
            throw new UnsupportedQueryException("Nonlocal album artist query.");
        using var context = new ArtistReadContext(database, options);
        using var transaction = database.BeginDeferredRead();
        var page = PageQueryNormalizer.Normalize(query is null ? new() { PageSize=96 } : new() { Page=query.Page,PageSize=query.PageSize ?? 96,Offset=query.Offset,TotalHint=query.TotalHint,FolderIds=query.FolderIds });
        var cte = AlbumArtistSql.Grouped(page.FolderIds.Count > 0, false);
        var search = query?.Search?.Trim() ?? "";
        var filter = search.Length == 0 ? "" : "WHERE instr(echo_artist_search_text(display_name),?) > 0";
        void Bind(SqliteCommand command, bool withSearch = true)
        {
            if (page.FolderIds.Count > 0) command.Add(System.Text.Json.JsonSerializer.Serialize(page.FolderIds));
            if (withSearch && search.Length > 0) command.Add(search.Normalize(NormalizationForm.FormKC).ToLowerInvariant());
        }
        if (Convert.ToInt64(CachedQuery.Scalar(database, cte + " SELECT COUNT(*) FROM ranked_names WHERE name_key IS NULL", c => Bind(c, false))) > 0)
            throw new UnsupportedQueryException("album_artist_sort_keys_not_ready");
        var total = page.TotalHint ?? Convert.ToInt64(CachedQuery.Scalar(database, cte + " SELECT COUNT(*) FROM grouped " + filter, c => Bind(c)));
        var items = new List<LibraryArtistDto>();
        using (var reader = CachedQuery.Reader(database, cte + $" SELECT {AlbumArtistSql.Select} FROM grouped {AlbumArtistSql.AvatarJoin} {filter} {AlbumArtistSql.Order(query?.Sort ?? "default", query?.PrioritizeArtistAvatars == true)} LIMIT ? OFFSET ?", command =>
        {
            Bind(command); command.Add(page.PageSize); command.Add(page.Offset);
        }))
        {
            var mapper = ArtistRowMapper.Bind(reader);
            while (reader.Read()) items.Add(AsGrouped(mapper.Map()));
        }
        return new() { Items=items,Page=page.Page,PageSize=page.PageSize,Total=total,HasMore=page.Offset+items.Count<total };
    }

    internal static LibraryArtistDto? Artist(ReadOnlyLibraryDatabase database, string key)
    {
        var cte = AlbumArtistSql.Grouped(false, true);
        using var reader = CachedQuery.Reader(database, cte + $" SELECT {AlbumArtistSql.Select} FROM grouped {AlbumArtistSql.AvatarJoin}", c => c.Add(key));
        return reader.Read() ? AsGrouped(ArtistRowMapper.Bind(reader).Map()) : null;
    }
    private static LibraryArtistDto AsGrouped(LibraryArtistDto artist)
    {
        artist.Id = "album-artist:" + Convert.ToHexString(Encoding.UTF8.GetBytes("l\u001f" + artist.ArtistKey)).ToLowerInvariant();
        return artist;
    }

    public static object Page(ReadOnlyLibraryDatabase database, string id, CoreLibraryPageQuery? query, CoreSearchOptions? options, bool albums)
    {
        var key = LocalKey(id) ?? throw new UnsupportedQueryException("Remote or malformed album artist.");
        using var context = new ArtistReadContext(database, options);
        using var transaction = database.BeginDeferredRead();
        // These detail endpoints intentionally accept page/pageSize/sort only.
        var page = PageQueryNormalizer.Normalize(new() { Page=query?.Page,PageSize=query?.PageSize ?? 96 });
        var sort = query?.Sort ?? "default";
        var predicate = $"tracks.missing=0 AND tracks.library_hidden=0 AND echo_artist_merge_key({AlbumArtistSql.Credit})=?";
        var scope = albums ? $"albums.id IN (SELECT album_tracks.album_id FROM album_tracks JOIN tracks ON tracks.id=album_tracks.track_id WHERE {predicate})" : predicate + ScopedTrackSearch.Predicate;
        var table = albums ? "albums" : "tracks";
        void Bind(SqliteCommand c) { c.Add(key); if (!albums) ScopedTrackSearch.Bind(c, query?.Search); }
        var total = Convert.ToInt64(CachedQuery.Scalar(database, $"SELECT COUNT(*) FROM {table} WHERE {scope}", Bind));
        var name = albums ? "title" : "title";
        var join = albums ? "LEFT JOIN local_album_sort_keys AS sort_keys ON sort_keys.album_id=albums.id" : "LEFT JOIN local_track_sort_keys AS sort_keys ON sort_keys.track_id=tracks.id";
        var title = $"sort_keys.title_key COLLATE NOCASE, {table}.{name} COLLATE NOCASE";
        var titleDesc = $"sort_keys.title_key COLLATE NOCASE DESC, {table}.{name} COLLATE NOCASE DESC";
        var order = sort switch
        {
            "titleDesc" => titleDesc,
            "yearAsc" when albums => $"albums.year IS NULL,albums.year ASC,{title}",
            "yearDesc" when albums => $"albums.year IS NULL,albums.year DESC,{title}",
            "recent" when albums => $"albums.created_at DESC,{title}",
            "durationAsc" when !albums => $"tracks.duration ASC,{title}",
            "durationDesc" when !albums => $"tracks.duration DESC,{title}",
            "random" when !albums => "RANDOM()",
            "title" or "titleAsc" => title,
            _ => albums ? title : $"sort_keys.album_key COLLATE NOCASE,tracks.album COLLATE NOCASE,tracks.disc_no,tracks.track_no,{title}",
        };
        if (Convert.ToInt64(CachedQuery.Scalar(database, $"SELECT COUNT(*) FROM {table} {join} WHERE ({scope}) AND sort_keys.{(albums ? "album_id" : "track_id")} IS NULL", Bind)) > 0)
            throw new UnsupportedQueryException("album_artist_sort_keys_not_ready");
        var ids = new List<string>();
        using (var reader = CachedQuery.Reader(database, $"SELECT {table}.id FROM {table} {join} WHERE {scope} ORDER BY {order} LIMIT ? OFFSET ?", c => { Bind(c);c.Add(page.PageSize);c.Add(page.Offset); }))
            while (reader.Read()) ids.Add(reader.GetString(0));
        if (albums)
        {
            var items = AlbumPageQuery.HydrateAlbums(database, ids);
            return new LibraryPageDto<LibraryAlbumDto> { Items=items,Page=page.Page,PageSize=page.PageSize,Total=total,HasMore=page.Offset+items.Count<total };
        }
        var tracks = TrackPageQuery.HydrateTracks(database, ids, false);
        return new LibraryPageDto<LibraryTrackListRowDto> { Items=tracks,Page=page.Page,PageSize=page.PageSize,Total=total,HasMore=page.Offset+tracks.Count<total };
    }
}
