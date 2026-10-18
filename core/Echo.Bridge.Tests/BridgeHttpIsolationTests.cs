using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Echo.Bridge.Library;
using Echo.Bridge.Link;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeHttpIsolationTests
{
    private static HttpClient Client(BridgeHttpServer server)
    {
        var client=new HttpClient { BaseAddress=new Uri($"http://127.0.0.1:{server.Port}"),Timeout=TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Add("X-ECHO-Link-Direct","1"); client.DefaultRequestHeaders.Add("X-ECHO-Link-Version","1");
        return client;
    }

    [Fact]
    public async Task FourMediaStreamsLeaveCatalogCapacityAndMediaPermitsAreReleased()
    {
        using var fixture=new BridgeFixture();
        using(var file=new FileStream(fixture.MediaPath,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)) file.SetLength(32*1024*1024);
        await using var server=new BridgeHttpServer(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)),"isolation","Bridge");
        await server.StartAsync(); using var client=Client(server);
        using var json=JsonDocument.Parse(await client.GetStringAsync("/echo-link/peer/v1/stream?id=kept"));
        var url=json.RootElement.GetProperty("track").GetProperty("streamUrl").GetString()!;
        var streams=new List<HttpResponseMessage>();
        try
        {
            for(var i=0;i<4;i++)
            {
                var stream=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);
                streams.Add(stream); Assert.Equal(HttpStatusCode.OK,stream.StatusCode);
            }
            using var catalog=await client.GetAsync("/echo-link/peer/v1/catalog?kind=albums&page=1");
            Assert.Equal(HttpStatusCode.OK,catalog.StatusCode);
            using var full=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.ServiceUnavailable,full.StatusCode);
            streams[0].Dispose();
            for(var i=0;i<100 && server.ActiveRequests>=4;i++) await Task.Delay(10);
            using var reopened=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK,reopened.StatusCode);
            using var device=await client.GetAsync("/echo-link/peer/v1/device");
            Assert.Equal(HttpStatusCode.OK,device.StatusCode);
        }
        finally { foreach(var stream in streams) stream.Dispose(); }
    }

    [Fact]
    public async Task ChineseAndLatinLyricsBothFitTheExistingWireLimit()
    {
        using var fixture=new BridgeFixture();
        await using var server=new BridgeHttpServer(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)),"unicode","Bridge");
        await server.StartAsync(); using var client=Client(server);
        foreach(var character in new[] { 'a','歌' })
        {
            var text=new string(character,25);
            var lines=JsonSerializer.Serialize(Enumerable.Range(0,4000).Select(n=>new { timeMs=n*1000,text }),
                new JsonSerializerOptions { Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            fixture.Execute("DELETE FROM lyrics_cache; INSERT INTO lyrics_cache(id,cache_key,track_id,provider,title,artist,kind,lines_json,offset_ms,provider_lyrics_id,created_at,updated_at) " +
                "VALUES('lyrics','local:kept','kept','local','Preserved','Artist','synced','"+lines+"',0,'cached','today','today');");
            using var response=await client.GetAsync("/echo-link/peer/v1/lyrics?id=kept");
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var bytes=await response.Content.ReadAsByteArrayAsync(); Assert.True(bytes.Length<=512*1024);
            using var body=JsonDocument.Parse(bytes);
            Assert.Equal(4000,body.RootElement.GetProperty("lyrics").GetProperty("lines").GetArrayLength());
            Assert.Equal(text,body.RootElement.GetProperty("lyrics").GetProperty("lines")[3999].GetProperty("text").GetString());
        }
    }
}
