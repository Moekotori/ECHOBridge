using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Bridge.Library;
using Echo.Bridge.Link;
using Echo.Core.Hosting;
using Echo.Core.Protocol;
using Echo.Core.Query;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgePlaylistReadTests
{
    private static void Seed(BridgeFixture fixture)
    {
        const string fields="{}";
        var sql = new StringBuilder("""
            INSERT INTO playlists(id,name,kind,source_provider,source_playlist_id,sort_mode,created_at,updated_at)
            VALUES('fav','Favorites','system','local','liked-tracks','manual','before','before'),
                  ('manual','Manual','manual','local',NULL,'titleAsc','before','before');
            """);
        for (var i=0; i<52; i++) sql.Append($"""
            INSERT INTO tracks(id,path,folder_id,size_bytes,mtime_ms,title,artist,album,album_artist,field_sources_json,created_at,updated_at,duration,codec)
            VALUES('track-{i}',$media||'-{i}.wav','folder',1024,1,'Track {i:D2}','Artist','Album','Artist','{fields}','before','before',{i+1},'pcm');
            INSERT INTO playlist_items(id,playlist_id,media_type,media_id,title_snapshot,position,added_at)
            VALUES('favorite-{i}','fav','track','track-{i}','Snapshot {i:D2}',{i},'before'),
                  ('manual-{i}','manual','track','track-{i}','Snapshot {i:D2}',{i},'before');
            INSERT INTO playlists(id,name,created_at,updated_at) VALUES('list-{i}','List {i:D2}','before','before');
            """);
        fixture.Execute(sql.ToString());
    }

    private static JsonElement Page(BridgeCatalog catalog, string kind, int page=1, string? sort=null, string search="", string? parentKind=null, string? parentId=null)
        => JsonSerializer.SerializeToElement(catalog.Page(kind,page,search,sort,parentKind,parentId,default));
    private static string First(JsonElement page) => page.GetProperty("entries")[0].GetProperty("title").GetString()!;

    [Fact]
    public async Task StaleFavoritesAndFolderTracksStayReadableWithoutChangingTheDesktopContractOrDatabase()
    {
        using var fixture=new BridgeFixture(); Seed(fixture);
        var reader=new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath));
        var catalog=new BridgeCatalog(reader);
        var hash=SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        Assert.Equal("Track 00",First(Page(catalog,"liked")));
        Assert.Equal("Track 51",First(Page(catalog,"liked",sort:"titleDesc")));
        Assert.Equal(52,Page(catalog,"liked",2).GetProperty("total").GetInt64());
        Assert.Equal(2,Page(catalog,"liked",2).GetProperty("entries").GetArrayLength());
        Assert.Equal(1,Page(catalog,"liked",search:"Snapshot 51").GetProperty("total").GetInt64());
        var folders=Page(catalog,"songs",2,parentKind:"folder",parentId:"root:folder");
        Assert.Equal(53,folders.GetProperty("total").GetInt64());
        Assert.Equal(3,folders.GetProperty("entries").GetArrayLength());
        Assert.Equal("Track 51",First(Page(catalog,"songs",sort:"titleDesc",parentKind:"folder",parentId:"root:folder")));
        Assert.Equal(1,Page(catalog,"songs",search:"Track 51",parentKind:"folder",parentId:"root:folder").GetProperty("total").GetInt64());
        using var strict=new RequestExecutor();
        strict.Execute(new() { Method="handshake",DatabaseGeneration=1,Params=new() { DatabasePath=fixture.DatabasePath,DatabaseGeneration=1 } });
        Assert.Throws<PlaylistSortKeysNotReadyException>(()=>strict.Execute(new() { Method="getFavoriteItems",DatabaseGeneration=1,
            Params=new() { PlaylistId="fav",Query=new() { SourceProvider="local",Sort="titleAsc" } } }));
        Assert.Throws<UnsupportedQueryException>(()=>strict.Execute(new() { Method="getFolderTracks",DatabaseGeneration=1,
            Params=new() { Query=new() { FolderId="folder",SourceProvider="local",Sort="titleAsc" } } }));
        await using var server=new BridgeHttpServer(reader,"playlist-reads","Bridge");
        await server.StartAsync();
        using var http=new HttpClient { BaseAddress=new Uri($"http://127.0.0.1:{server.Port}") };
        http.DefaultRequestHeaders.Add("X-ECHO-Link-Direct","1"); http.DefaultRequestHeaders.Add("X-ECHO-Link-Version","1");
        foreach(var query in new[] { "kind=liked", "kind=songs&parentKind=folder&parentId=root:folder" })
        { using var response=await http.GetAsync("/echo-link/peer/v1/catalog?page=1&"+query); Assert.Equal(HttpStatusCode.OK,response.StatusCode); }
        Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)));
        fixture.Execute("""
            INSERT INTO local_playlist_sort_keys(item_id,title_key,title_text,artist_key,artist_text,album_key,album_text,item_updated_at)
            SELECT id,printf('%02d',51-position),title_snapshot,'Artist','Artist','Album','Album','before' FROM playlist_items;
            INSERT OR REPLACE INTO library_maintenance_flags(key,value,updated_at)
            SELECT 'local_playlist_sort_keys.synced_revision','libraryTextSortKey/v1|rev:'||revision,'after' FROM local_playlist_sort_key_revision;
            """);
        Assert.Equal("Track 51",First(Page(catalog,"liked")));
    }

    [Fact]
    public void RequestedItemSortRunsBeforePagingAndPreservesTheSavedDesktopSort()
    {
        using var fixture=new BridgeFixture(); Seed(fixture);
        var hash=SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        var catalog=new BridgeCatalog(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)));
        Assert.Equal("Track 51",First(Page(catalog,"songs",sort:"titleDesc",parentKind:"playlist",parentId:"manual")));
        Assert.Equal("Track 01",First(Page(catalog,"songs",2,"titleDesc",parentKind:"playlist",parentId:"manual")));
        Assert.Equal("Track 00",First(Page(catalog,"songs",sort:"titleAsc",parentKind:"playlist",parentId:"manual")));
        using var strict=new RequestExecutor();
        strict.Execute(new() { Method="handshake",DatabaseGeneration=1,Params=new() { DatabasePath=fixture.DatabasePath,DatabaseGeneration=1 } });
        var saved=(LibraryPageDto<LibraryPlaylistItemDto>)strict.Execute(new() { Method="getPlaylistItems",DatabaseGeneration=1,
            Params=new() { PlaylistId="manual",Query=new() { Sort="titleDesc" } } })!;
        Assert.Equal("Snapshot 00",saved.Items[0].TitleSnapshot);
        Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)));
    }

    [Fact]
    public void PlaylistHeadersSortAndSearchBeforePaging()
    {
        using var fixture=new BridgeFixture(); Seed(fixture);
        fixture.Execute("INSERT INTO playlists(id,name,created_at,updated_at) VALUES('accent','Été 100%','before','before');");
        var hash=SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        var catalog=new BridgeCatalog(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)));
        Assert.Equal("List 00",First(Page(catalog,"playlists",sort:"titleAsc",search:"List ")));
        Assert.Equal("List 51",First(Page(catalog,"playlists",sort:"titleDesc",search:"List ")));
        var second=Page(catalog,"playlists",2,"titleDesc","List ");
        Assert.Equal(52,second.GetProperty("total").GetInt64());
        Assert.Equal("List 01",First(second));
        Assert.Equal(2,second.GetProperty("entries").GetArrayLength());
        Assert.Equal(1,Page(catalog,"playlists",search:"List 51").GetProperty("total").GetInt64());
        Assert.Equal(1,Page(catalog,"playlists",search:"été 100%").GetProperty("total").GetInt64());
        Assert.Equal(1,Page(catalog,"playlists",search:"%").GetProperty("total").GetInt64());
        Assert.Equal(54,Page(catalog,"playlists").GetProperty("total").GetInt64());
        Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)));
    }
}
