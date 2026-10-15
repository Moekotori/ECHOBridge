using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Echo.Bridge.Library;
using Echo.Bridge.Link;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeAssetHttpTests
{
    [Fact]
    public async Task CoverRequestsSelectAlbumAndLargeInsteadOfTheSmallestThumbnail()
    {
        using var fixture = new BridgeFixture();
        var cover = Path.Combine(fixture.Root, "cover-cache");
        Directory.CreateDirectory(cover);
        var thumb = Raster(32, 1); var album = Raster(64, 2); var large = Raster(128, 3);
        File.WriteAllBytes(Path.Combine(cover, "thumb.png"), thumb);
        File.WriteAllBytes(Path.Combine(cover, "album.png"), album);
        File.WriteAllBytes(Path.Combine(cover, "large.png"), large);
        fixture.Execute("INSERT INTO covers(id,source_type,thumb_path,album_path,large_path,created_at,updated_at) VALUES('art','embedded','thumb.png','album.png','large.png','today','today');");
        await using var server = Server(fixture);
        await server.StartAsync();
        using var client = Client(server);
        foreach (var query in new[] { "", "&size=160", "&size=320" })
            Assert.Equal(album, await Image(client, "art" + query));
        Assert.Equal(large, await Image(client, "art&size=512"));

        // The desktop peer protocol retains its 256 KiB image bound, with album fallback for a large image.
        File.WriteAllBytes(Path.Combine(cover, "large.png"), Raster(256 * 1024 + 1, 3));
        Assert.Equal(album, await Image(client, "art&size=512"));
        File.WriteAllBytes(Path.Combine(cover, "album.png"), Raster(256 * 1024 + 1, 2));
        using var oversized = await client.GetAsync("/echo-link/peer/v1/thumb?id=art&size=512");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [Fact]
    public async Task CoversRemainInsideTheCacheAndRejectNonRasterData()
    {
        using var fixture = new BridgeFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "cover-cache"));
        File.WriteAllBytes(Path.Combine(fixture.Root, "private.png"), Raster(32, 1));
        File.WriteAllText(Path.Combine(fixture.Root, "cover-cache", "vector.svg"), "<svg/>");
        fixture.Execute("INSERT INTO covers(id,source_type,album_path,created_at,updated_at) VALUES('outside','embedded','../private.png','today','today'),('vector','embedded','vector.svg','today','today');");
        await using var server = Server(fixture);
        await server.StartAsync();
        using var client = Client(server);
        Assert.Null(await Image(client, "outside&size=512"));
        Assert.Null(await Image(client, "vector&size=320"));
    }

    [Theory]
    [InlineData(".lrc", "[00:01.250]First\n[00:02.500]Second", "synced", 1250)]
    [InlineData(".txt", "First\n\nSecond", "plain", -1)]
    public async Task LocalLyricsWithoutACachedMatchProvideDesktopDisplayLines(string extension, string text, string kind, int time)
    {
        using var fixture = new BridgeFixture();
        var sidecar = Path.ChangeExtension(fixture.MediaPath, extension);
        File.WriteAllText(sidecar, text);
        var original = SHA256.HashData(File.ReadAllBytes(sidecar));
        var database = SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        await using var server = Server(fixture);
        await server.StartAsync();
        using var client = Client(server);
        using var document = JsonDocument.Parse(await client.GetStringAsync("/echo-link/peer/v1/lyrics?id=kept"));
        var lyrics = document.RootElement.GetProperty("lyrics");
        Assert.Equal(kind, lyrics.GetProperty("kind").GetString());
        Assert.Equal(2, lyrics.GetProperty("lines").GetArrayLength());
        Assert.Equal(time, lyrics.GetProperty("lines")[0].GetProperty("timeMs").GetInt32());
        Assert.Equal("First", lyrics.GetProperty("lines")[0].GetProperty("text").GetString());
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(sidecar)));
        Assert.Equal(database, SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)));
    }

    [Fact]
    public async Task CachedRichLyricsKeepTimingAndTranslationsWithoutPrivateFields()
    {
        using var fixture = new BridgeFixture();
        fixture.Execute("""
            INSERT INTO lyrics_cache(id,cache_key,track_id,provider,title,artist,kind,lines_json,offset_ms,provider_lyrics_id,created_at,updated_at)
            VALUES('lyrics','local:kept','kept','local','Preserved','Artist','synced',
            '[{"timeMs":1250,"endMs":2000,"text":"First","translation":"Translated","romanization":"First","words":[{"startMs":1250,"endMs":2000,"text":"First"}],"localPath":"private-value"}]',
            -120,'cached','today','today');
            """);
        await using var server = Server(fixture);
        await server.StartAsync();
        using var client = Client(server);
        var body = await client.GetStringAsync("/echo-link/peer/v1/lyrics?id=kept");
        using var document = JsonDocument.Parse(body);
        var lyrics = document.RootElement.GetProperty("lyrics");
        Assert.Equal(-120, lyrics.GetProperty("offsetMs").GetInt32());
        var line = lyrics.GetProperty("lines")[0];
        Assert.Equal("Translated", line.GetProperty("translation").GetString());
        Assert.Equal(1250, line.GetProperty("words")[0].GetProperty("startMs").GetInt32());
        Assert.DoesNotContain("private-value", body);
        Assert.DoesNotContain(fixture.Root, body);
    }

    [Fact]
    public async Task ManualRemovalDoesNotResurrectALocalSidecar()
    {
        using var fixture = new BridgeFixture();
        File.WriteAllText(Path.ChangeExtension(fixture.MediaPath, ".lrc"), "[00:01]Removed");
        fixture.Execute("INSERT INTO lyrics_cache(id,cache_key,track_id,provider,title,artist,kind,lines_json,provider_lyrics_id,acceptance_origin,created_at,updated_at) VALUES('lyrics','manual:kept','kept','manual','Preserved','Artist','empty','[]','removed:manual','manual','today','today');");
        await using var server = Server(fixture);
        await server.StartAsync();
        using var client = Client(server);
        using var document = JsonDocument.Parse(await client.GetStringAsync("/echo-link/peer/v1/lyrics?id=kept"));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("lyrics").ValueKind);
    }

    private static BridgeHttpServer Server(BridgeFixture fixture) => new(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)), "bridge-assets", "Assets");
    private static HttpClient Client(BridgeHttpServer server)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}"), Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Add("X-ECHO-Link-Direct", "1");
        client.DefaultRequestHeaders.Add("X-ECHO-Link-Version", "1");
        return client;
    }
    private static async Task<byte[]?> Image(HttpClient client, string query)
    {
        using var document = JsonDocument.Parse(await client.GetStringAsync("/echo-link/peer/v1/thumb?id=" + query));
        var image = document.RootElement.GetProperty("image");
        return image.ValueKind == JsonValueKind.Null ? null : Convert.FromBase64String(image.GetProperty("bytes").GetString()!);
    }
    private static byte[] Raster(int length, byte marker)
    {
        var bytes = new byte[length];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        bytes[^1] = marker;
        return bytes;
    }
}
