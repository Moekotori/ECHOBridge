using System.Security.Cryptography;
using System.Text.Json;
using Echo.Bridge.Library;
using Echo.Bridge.Link;
using Echo.Core.Protocol;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeLibraryTests
{
    [Fact]
    public void CoreReadsPreserveIdentityObserveLaterWritesAndNeverAcceptMutations()
    {
        using var fixture = new BridgeFixture();
        var reader = new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath));
        var hash = SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        Assert.Equal("Preserved", reader.Track("kept").Title);
        Assert.Throws<BridgeException>(() => reader.Read("libraryMutation"));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)));
        fixture.Execute("UPDATE tracks SET title='Changed' WHERE id='kept'");
        Assert.Equal("Changed", reader.Track("kept").Title);
        // Connection release allows immediate exclusive file access / maintenance between requests.
        using var exclusive = new FileStream(fixture.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.None);
    }

    [Fact]
    public void MissingLibraryIsNotCreatedAndFolderPayloadDoesNotExposePaths()
    {
        using var fixture = new BridgeFixture();
        var missing = Path.Combine(fixture.Root, "missing.sqlite");
        Assert.Throws<BridgeException>(() => BridgeProfile.Open(missing));
        Assert.False(File.Exists(missing));
        var catalog = new BridgeCatalog(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)));
        var response = JsonSerializer.Serialize(catalog.Page("folders", 1, "", null, null, null, default));
        Assert.Contains("root:folder", response);
        Assert.DoesNotContain(fixture.Root.Replace("\\", "\\\\"), response);
        Assert.DoesNotContain("databasePath", response);
    }

    [Fact]
    public void MediaCapabilitiesArePrivateRevocableAndResolveCueFragmentsToPhysicalAudio()
    {
        using var fixture = new BridgeFixture();
        var track = new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)).Track("kept");
        var tokens = new BridgeMediaTokens();
        var first = System.Net.IPAddress.Parse("192.168.1.2"); var other = System.Net.IPAddress.Parse("192.168.1.3");
        var token = tokens.Issue(track, first);
        Assert.Equal(fixture.MediaPath, tokens.Resolve(token, first));
        Assert.Throws<BridgeException>(() => tokens.Resolve(token, other));
        tokens.Clear(); Assert.Throws<BridgeException>(() => tokens.Resolve(token, first));
        var cuePath = Path.Combine(fixture.Root, "album.cue");
        File.WriteAllText(cuePath, "FILE \"source.wav\" WAVE\n  TRACK 01 AUDIO\n    INDEX 01 00:00:00\n  TRACK 02 AUDIO\n    INDEX 01 00:01:00\n");
        track.Path = cuePath + "#cueTrack=1";
        Assert.True(BridgeTrackProjection.CanPlay(track));
        var source = BridgeTrackSource.Resolve(track);
        Assert.Equal(fixture.MediaPath, source.Path);
        Assert.Equal(0, source.Range!.startSeconds);
        Assert.Equal(1, source.Range.endSeconds);
        Assert.Equal(fixture.MediaPath, tokens.Resolve(tokens.Issue(track, source, first), first));
    }

    [Fact]
    public void RangeSupportsSeekAndRejectsMalformedOrUnsatisfiableRequests()
    {
        Assert.Equal((100L, 100L, true), BridgeMediaResponse.Range("bytes=100-199", 1024));
        Assert.Equal((1014L, 10L, true), BridgeMediaResponse.Range("bytes=-10", 1024));
        Assert.Equal((100L, 924L, true), BridgeMediaResponse.Range("bytes=100-", 1024));
        Assert.Throws<BridgeException>(() => BridgeMediaResponse.Range("bytes=1024-", 1024));
        Assert.Throws<BridgeException>(() => BridgeMediaResponse.Range("bytes=0-1,5-6", 1024));
        Assert.False(BridgeLan.IsAllowed(System.Net.IPAddress.Parse("2001:db8::c0a8:0102")));
    }
}
