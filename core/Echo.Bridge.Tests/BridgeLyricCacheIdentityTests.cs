using System.Security.Cryptography;
using System.Text.Json;
using Echo.Bridge.Library;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeLyricCacheIdentityTests
{
    private static JsonElement Read(BridgeFixture fixture)
    {
        var hash=SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        var result=JsonSerializer.SerializeToElement(BridgeLyrics.Read(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)),"kept",default));
        Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath))); return result;
    }
    private static string Text(JsonElement lyrics) => lyrics.GetProperty("lines")[0].GetProperty("text").GetString()!;

    [Fact]
    public void StaleAutomaticCacheDoesNotMaskSidecarsOrAnotherMatchingCache()
    {
        using var fixture=new BridgeFixture();
        fixture.Execute("""
            INSERT INTO lyrics_cache(id,cache_key,track_id,provider,title,artist,kind,lines_json,acceptance_origin,created_at,updated_at)
            VALUES('stale','local|old song|old artist|album|13|','kept','local','Old Song','Old Artist','synced',
            '[{"timeMs":1000,"text":"Wrong lyrics"}]','auto','before','later');
            """);
        Assert.Equal(JsonValueKind.Null,Read(fixture).ValueKind);
        var sidecar=Path.ChangeExtension(fixture.MediaPath,".lrc"); File.WriteAllText(sidecar,"[00:01]Sidecar");
        var hash=SHA256.HashData(File.ReadAllBytes(sidecar));
        Assert.Equal("Sidecar",Text(Read(fixture)));
        fixture.Execute("""
            INSERT INTO lyrics_cache(id,cache_key,track_id,provider,title,artist,kind,lines_json,acceptance_origin,created_at,updated_at)
            VALUES('matched','lrclib|preserved|artist|album|13|','kept','lrclib','Preserved','Artist','synced',
            '[{"timeMs":1000,"text":"Matched cache"}]','auto','before','before');
            """);
        Assert.Equal("Matched cache",Text(Read(fixture)));
        Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(sidecar)));
    }

    [Theory]
    [InlineData("manual","auto")]
    [InlineData("local","manual")]
    public void ExplicitManualChoiceRemainsAuthoritative(string provider,string origin)
    {
        using var fixture=new BridgeFixture();
        fixture.Execute($$"""
            INSERT INTO lyrics_cache(id,cache_key,track_id,provider,title,artist,kind,lines_json,acceptance_origin,created_at,updated_at)
            VALUES('manual','{{provider}}|other song|other artist|album|13|','kept','{{provider}}','Other Song','Other Artist','synced',
            '[{"timeMs":1000,"text":"Manual choice"}]','{{origin}}','before','before');
            """);
        Assert.Equal("Manual choice",Text(Read(fixture)));
    }

    [Fact]
    public void StoredQueryIdentityKeepsAcceptedCoversWithDifferentProviderMetadata()
    {
        using var fixture=new BridgeFixture();
        fixture.Execute("""
            UPDATE tracks SET title='Ｓｏｎｇ 🎵',artist='歌手' WHERE id='kept';
            INSERT INTO lyrics_cache(id,cache_key,track_id,provider,title,artist,kind,lines_json,acceptance_origin,created_at,updated_at)
            VALUES('cover','lrclib|song|歌手|album|13|cover','kept','lrclib','Song','Original Singer','synced',
            '[{"timeMs":1000,"text":"Accepted cover"}]','auto','before','before');
            """);
        Assert.Equal("Accepted cover",Text(Read(fixture)));
    }
}
