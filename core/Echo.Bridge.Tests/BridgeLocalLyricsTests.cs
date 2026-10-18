using System.Text.Json;
using Echo.Bridge.Library;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeLocalLyricsTests
{
    [Theory]
    [InlineData(500,500,1500)]
    [InlineData(-500,1500,2500)]
    public void ExpandsTimestampsAndAppliesOffsetOnceInChronologicalOrder(int offset,int first,int second)
    {
        using var fixture=new BridgeFixture();
        var text=$"[offset:{offset}]\n[ar:Artist]\n[00:03.000]Last\n[00:01.000][00:02.000]Hello";
        var file=Path.ChangeExtension(fixture.MediaPath,".lrc"); File.WriteAllText(file,text);
        var lyrics=JsonSerializer.SerializeToElement(BridgeLyrics.Read(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)),"kept",default));
        var lines=lyrics.GetProperty("lines");
        Assert.Equal(3,lines.GetArrayLength()); Assert.Equal(0,lyrics.GetProperty("offsetMs").GetInt32());
        Assert.Equal(first,lines[0].GetProperty("timeMs").GetInt64());
        Assert.Equal(second,lines[1].GetProperty("timeMs").GetInt64());
        Assert.Equal("Hello",lines[0].GetProperty("text").GetString());
        Assert.Equal("Hello",lines[1].GetProperty("text").GetString());
        Assert.Equal("Last",lines[2].GetProperty("text").GetString());
        Assert.Equal(text,File.ReadAllText(file));
    }

    [Fact]
    public void ExpandedLinesAndTextRemainBounded()
    {
        using var fixture=new BridgeFixture();
        var file=Path.ChangeExtension(fixture.MediaPath,".lrc");
        File.WriteAllText(file,string.Join('\n',Enumerable.Repeat("[00:01][00:02][00:03][00:04]a",1000)));
        var reader=new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath));
        Assert.Equal(4000,JsonSerializer.SerializeToElement(BridgeLyrics.Read(reader,"kept",default)).GetProperty("lines").GetArrayLength());
        File.AppendAllText(file,"\n[00:05]b");
        Assert.Equal("peer_lyrics_too_large",Assert.Throws<BridgeException>(()=>BridgeLyrics.Read(reader,"kept",default)).Message);
        File.WriteAllText(file,string.Concat(Enumerable.Repeat("[00:01]",25))+new string('x',4001));
        Assert.Throws<BridgeException>(()=>BridgeLyrics.Read(reader,"kept",default));
    }
}
