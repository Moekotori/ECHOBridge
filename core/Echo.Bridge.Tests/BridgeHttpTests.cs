using System.Net;
using System.Text.Json;
using Echo.Bridge.Library;
using Echo.Bridge.Link;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeHttpTests
{
    [Fact]
    public void ExportsActualBridgeDiscoveryPacketForTheExistingDesktopParser()
    {
        var packet = BridgeMdnsPacket.Announcement("bridge-source", "Fixture Bridge", IPAddress.Parse("192.168.1.8"), 28000);
        Assert.Equal(5, System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6, 2)));
        var evidence = Environment.GetEnvironmentVariable("ECHO_BRIDGE_TEST_EVIDENCE_ROOT");
        if (evidence is null) return;
        Directory.CreateDirectory(evidence);
        File.WriteAllBytes(Path.Combine(evidence, "announcement.bin"), packet);
    }

    [Fact]
    public async Task ActualHttpServesCoreCatalogAndOriginalRangeWhileRejectingBrowserAndWriteAccess()
    {
        using var fixture = new BridgeFixture();
        await using var server = new BridgeHttpServer(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)), "bridge-test", "Fixture Bridge");
        await server.StartAsync(0);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}"), Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Add("X-ECHO-Link-Direct", "1"); client.DefaultRequestHeaders.Add("X-ECHO-Link-Version", "1");
        var catalog = await client.GetStringAsync("/echo-link/peer/v1/catalog?kind=folders&page=1");
        Assert.Contains("root:folder", catalog); Assert.DoesNotContain("databasePath", catalog);
        using var stream = JsonDocument.Parse(await client.GetStringAsync("/echo-link/peer/v1/stream?id=kept"));
        var url = stream.RootElement.GetProperty("track").GetProperty("streamUrl").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Range = new(100, 199);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(File.ReadAllBytes(fixture.MediaPath)[100..200], await response.Content.ReadAsByteArrayAsync());
        using var browser = new HttpRequestMessage(HttpMethod.Get, "/echo-link/peer/v1/device"); browser.Headers.Add("Origin", "http://untrusted.invalid");
        using var denied = await client.SendAsync(browser); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var write = await client.PostAsync("/echo-link/v1/playback/command", null); Assert.Equal(HttpStatusCode.MethodNotAllowed, write.StatusCode);
        using var traversal = await client.GetAsync("/echo-link/peer/v1/stream?id=../../private"); Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
    }
}
