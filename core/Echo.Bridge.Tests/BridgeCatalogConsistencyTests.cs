using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Bridge.Library;
using Echo.Core.Hosting;
using Echo.Core.Protocol;
using Echo.Core.Query;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeCatalogConsistencyTests
{
    private static void Seed(BridgeFixture fixture)
    {
        var sql = new StringBuilder("INSERT INTO artists(id,artist_key,name,created_at,updated_at) VALUES('artist','artist','Artist','before','before');");
        const string fields = "{}";
        for (var i = 0; i < 52; i++)
            sql.Append($"""
                INSERT INTO tracks(id,path,folder_id,size_bytes,mtime_ms,title,artist,album,album_artist,field_sources_json,created_at,updated_at,duration,codec,genre)
                VALUES('song-{i}',$media || '-{i}.wav','folder',1024,1,'Song {i:D2}','Artist','Album {i:D2}','Artist','{fields}','before','before',12.5,'pcm','Rock');
                INSERT INTO albums(id,album_key,title,album_artist,track_count,created_at,updated_at)
                VALUES('album-{i}','album-{i}','Album {i:D2}','Artist',1,'before','before');
                INSERT INTO album_tracks(album_id,track_id) VALUES('album-{i}','song-{i}');
                INSERT INTO artist_tracks(artist_id,track_id,source_name) VALUES('artist','song-{i}','Artist');
                INSERT INTO artist_albums(artist_id,album_id,source_name) VALUES('artist','album-{i}','Artist');
                """);
        fixture.Execute(sql.ToString());
    }
    private static JsonElement Page(BridgeCatalog catalog, string kind, int page = 1, string search = "", string? sort = null,
        string? parentKind = null, string? parentId = null) => JsonSerializer.SerializeToElement(catalog.Page(kind,page,search,sort,parentKind,parentId,default));

    [Fact]
    public void StaleIndexesAllowReadOnlyBridgePagesButKeepTheDesktopWriterContract()
    {
        using var fixture = new BridgeFixture(); Seed(fixture);
        var before = SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        var catalog = new BridgeCatalog(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)));
        var first = Page(catalog,"albums"); var second = Page(catalog,"albums",2);
        Assert.Equal(52, first.GetProperty("total").GetInt64());
        Assert.Equal(50, first.GetProperty("entries").GetArrayLength());
        Assert.Equal("Album 00", first.GetProperty("entries")[0].GetProperty("title").GetString());
        Assert.Equal("Album 50", second.GetProperty("entries")[0].GetProperty("title").GetString());
        Assert.Equal("Album 51", Page(catalog,"albums",sort:"titleDesc").GetProperty("entries")[0].GetProperty("title").GetString());
        Assert.Equal(1, Page(catalog,"artists").GetProperty("total").GetInt64());
        Assert.Equal(2, Page(catalog,"albums",2,parentKind:"artist",parentId:"artist").GetProperty("entries").GetArrayLength());
        using var strict = new RequestExecutor();
        strict.Execute(new() { Method="handshake",DatabaseGeneration=1,Params=new() { DatabasePath=fixture.DatabasePath,DatabaseGeneration=1 } });
        Assert.Throws<UnsupportedQueryException>(() => strict.Execute(new() { Method="getAlbums",DatabaseGeneration=1,
            Params=new() { Query=new() { SourceProvider="local",Sort="titleAsc" } } }));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)));
    }

    [Fact]
    public void ScopedSearchFiltersBeforeTotalsAndPagesAndTreatsWildcardsLiterally()
    {
        using var fixture = new BridgeFixture(); Seed(fixture);
        var catalog = new BridgeCatalog(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)));
        Assert.Equal(52, Page(catalog,"songs",parentKind:"genre",parentId:"rock").GetProperty("total").GetInt64());
        var genre = Page(catalog,"songs",search:"Song 51",parentKind:"genre",parentId:"rock");
        Assert.Equal(1, genre.GetProperty("total").GetInt64());
        Assert.Equal("song-51", genre.GetProperty("entries")[0].GetProperty("id").GetString());
        Assert.Equal(0, Page(catalog,"songs",search:"%",parentKind:"genre",parentId:"rock").GetProperty("total").GetInt64());
        var albums = Page(catalog,"albums",search:"Album 51",parentKind:"artist",parentId:"artist");
        Assert.Equal(1, albums.GetProperty("total").GetInt64());
        Assert.Equal("album-51", albums.GetProperty("entries")[0].GetProperty("id").GetString());
        Assert.Empty(Page(catalog,"albums",2,"Album 51",parentKind:"artist",parentId:"artist").GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public void StaleTrackAndGenreIndexesRemainReadableWithSearchAndPagination()
    {
        using var fixture=new BridgeFixture(); Seed(fixture);
        var before=SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        var catalog=new BridgeCatalog(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)));
        var first=Page(catalog,"songs"); var second=Page(catalog,"songs",2);
        Assert.Equal(53,first.GetProperty("total").GetInt64());
        Assert.Equal(50,first.GetProperty("entries").GetArrayLength());
        Assert.Equal(3,second.GetProperty("entries").GetArrayLength());
        var ids=first.GetProperty("entries").EnumerateArray().Concat(second.GetProperty("entries").EnumerateArray())
            .Select(row=>row.GetProperty("id").GetString()).ToArray();
        Assert.Equal(53,ids.Distinct().Count());
        Assert.Equal("song-51",Page(catalog,"songs",sort:"titleDesc").GetProperty("entries")[0].GetProperty("id").GetString());
        Assert.Equal(1,Page(catalog,"songs",search:"Song 51").GetProperty("total").GetInt64());
        var genres=Page(catalog,"genres");
        Assert.Equal(2,genres.GetProperty("total").GetInt64());
        Assert.Equal("Rock",genres.GetProperty("entries")[0].GetProperty("title").GetString());
        Assert.Equal(52,genres.GetProperty("entries")[0].GetProperty("count").GetInt64());
        Assert.Equal(1,Page(catalog,"genres",search:"Rock").GetProperty("total").GetInt64());
        Assert.Equal(0,Page(catalog,"genres",search:"%").GetProperty("total").GetInt64());
        using var strict=new RequestExecutor();
        strict.Execute(new() { Method="handshake",DatabaseGeneration=1,Params=new() { DatabasePath=fixture.DatabasePath,DatabaseGeneration=1 } });
        foreach(var method in new[] { "getTracks","getGenres" })
            Assert.Throws<UnsupportedQueryException>(()=>strict.Execute(new() { Method=method,DatabaseGeneration=1,
                Params=new() { Query=new() { SourceProvider="local",Sort="titleAsc" } } }));
        Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)));
    }

    [Fact]
    public void ArtistAlbumsUseTheSameVisibleTrackConditionAsTheAlbumWall()
    {
        using var fixture=new BridgeFixture(); Seed(fixture);
        fixture.Execute("UPDATE tracks SET missing=1 WHERE id='song-0'; UPDATE tracks SET library_hidden=1 WHERE id='song-1';");
        var catalog=new BridgeCatalog(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)));
        var wall=Page(catalog,"albums"); var artist=Page(catalog,"albums",parentKind:"artist",parentId:"artist");
        Assert.Equal(50,wall.GetProperty("total").GetInt64());
        Assert.Equal(wall.GetProperty("total").GetInt64(),artist.GetProperty("total").GetInt64());
        Assert.Equal(50,artist.GetProperty("entries").GetArrayLength());
        Assert.Empty(Page(catalog,"albums",2,parentKind:"artist",parentId:"artist").GetProperty("entries").EnumerateArray());
        Assert.Equal(0,Page(catalog,"albums",search:"Album 00",parentKind:"artist",parentId:"artist").GetProperty("total").GetInt64());
    }

    [Fact]
    public void CueAvailabilityAndPhoneFormatsFollowThePhysicalSourceWithoutBlockingOtherRows()
    {
        using var fixture = new BridgeFixture();
        var cue = Path.Combine(fixture.Root,"album.cue");
        File.WriteAllText(cue,"FILE \"source.wav\" WAVE\n TRACK 01 AUDIO\n INDEX 01 00:00:00\n");
        var reader = new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath));
        var track = reader.Track("kept"); track.Path=cue+"#cueTrack=1"; track.Codec=null;
        Assert.True((bool)BridgeTrackProjection.Entry(track)["canPlay"]!);
        File.WriteAllText(cue,"FILE \"missing.flac\" WAVE\n TRACK 01 AUDIO\n INDEX 01 00:00:00\n");
        Assert.False(BridgeTrackProjection.CanPlay(track));
        Assert.Throws<BridgeException>(() => BridgeTrackSource.Resolve(track));
        File.WriteAllText(Path.Combine(fixture.Root,"source.dff"),"test");
        File.WriteAllText(cue,"FILE \"source.dff\" WAVE\n TRACK 01 AUDIO\n INDEX 01 00:00:00\n");
        Assert.False(BridgeTrackProjection.CanPlay(track));
        track=reader.Track("kept");
        Assert.True((bool)BridgeTrackProjection.Entry(track)["canPlayOnPhone"]!);
        track.Path=Path.Combine(fixture.Root,"source.tta"); track.Codec="tta"; File.WriteAllText(track.Path,"test");
        var entry = BridgeTrackProjection.Entry(track);
        Assert.True((bool)entry["canPlay"]!); Assert.False((bool)entry["canPlayOnPhone"]!);
    }
}
