using System.Text.Json;
using Echo.Bridge.Library;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeLyricsLimitsTests
{
    private static void Cache(BridgeFixture fixture, object lines)
    {
        var json = JsonSerializer.Serialize(lines);
        fixture.Execute("DELETE FROM lyrics_cache; INSERT INTO lyrics_cache(id,cache_key,track_id,provider,title,artist,kind,lines_json,offset_ms,provider_lyrics_id,created_at,updated_at) " +
            "VALUES('lyrics','local:kept','kept','local','Preserved','Artist','synced','"+json+"',-120,'cached','today','today');");
    }
    [Fact]
    public void CachedLyricsKeep4000LinesAnd1000WordsWhileRejectingTheNextLine()
    {
        using var fixture = new BridgeFixture();
        var reader = new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath));
        var words = Enumerable.Range(0,1000).Select(n => new { text="w", startMs=n, endMs=n+1 }).ToArray();
        Cache(fixture, Enumerable.Range(0,4000).Select(n => new { timeMs=n*1000,text="Line",words=n==0 ? words : null }));
        var result = JsonSerializer.SerializeToElement(BridgeLyrics.Read(reader,"kept",default));
        Assert.Equal(4000,result.GetProperty("lines").GetArrayLength());
        Assert.Equal(1000,result.GetProperty("lines")[0].GetProperty("words").GetArrayLength());
        Assert.Equal(-120,result.GetProperty("offsetMs").GetInt32());
        Cache(fixture, Enumerable.Range(0,4001).Select(n => new { timeMs=n,text="Line" }));
        Assert.Equal("peer_lyrics_too_large",Assert.Throws<BridgeException>(()=>BridgeLyrics.Read(reader,"kept",default)).Message);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsTotalWordOrCharacterOverflow(bool wordOverflow)
    {
        using var fixture = new BridgeFixture();
        if (wordOverflow) Cache(fixture, Enumerable.Range(0,17).Select(n => new { timeMs=n,text="",words=Enumerable.Range(0,n==16 ? 1 : 1000).Select(_=>new { text="w",startMs=0 }) }));
        else Cache(fixture, Enumerable.Range(0,25).Select(n => new { timeMs=n,text=new string('x',4001) }));
        var reader = new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath));
        Assert.Equal("peer_lyrics_too_large",Assert.Throws<BridgeException>(()=>BridgeLyrics.Read(reader,"kept",default)).Message);
    }
    [Fact]
    public void SidecarLyricsReturnEveryAllowedLineAndRejectOverflowWithoutChangingTheFile()
    {
        using var fixture = new BridgeFixture();
        var path=Path.ChangeExtension(fixture.MediaPath,".lrc");
        var text=string.Join('\n',Enumerable.Repeat("[00:01]Line",4000)); File.WriteAllText(path,text);
        var reader=new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath));
        Assert.Equal(4000,JsonSerializer.SerializeToElement(BridgeLyrics.Read(reader,"kept",default)).GetProperty("lines").GetArrayLength());
        Assert.Equal(text,File.ReadAllText(path));
        File.AppendAllText(path,"\n[00:02]Overflow");
        Assert.Throws<BridgeException>(()=>BridgeLyrics.Read(reader,"kept",default));
        Assert.EndsWith("Overflow",File.ReadAllText(path));
        File.WriteAllText(path,"[00:01]"+new string('x',4096));
        Assert.Equal(4096,JsonSerializer.SerializeToElement(BridgeLyrics.Read(reader,"kept",default)).GetProperty("lines")[0].GetProperty("text").GetString()!.Length);
        File.AppendAllText(path,"x");
        Assert.Throws<BridgeException>(()=>BridgeLyrics.Read(reader,"kept",default));
    }
}
