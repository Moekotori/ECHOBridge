using Echo.Bridge.Library;
using Echo.Bridge.Link;

namespace Echo.Bridge;

public sealed class BridgeSession : IAsyncDisposable
{
    public BridgeHttpServer Server { get; }
    public BridgeMdnsAdvertiser Advertiser { get; }
    private BridgeSession(BridgeHttpServer server, BridgeMdnsAdvertiser advertiser) { Server = server; Advertiser = advertiser; }
    public static async Task<BridgeSession> StartAsync(string databasePath, string id, string name, int port = 0)
    {
        var reader = new BridgeLibraryReader(BridgeProfile.Open(databasePath));
        // Probe a real bounded query before announcing. This never initializes or upgrades the database.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await Task.Run(() => reader.Read("getTracksByIds", new() { TrackIds = ["bridge-read-probe-" + Guid.NewGuid().ToString("N")] }, deadline.Token), deadline.Token);
        var server = new BridgeHttpServer(reader, id, name);
        await server.StartAsync(port);
        var advertiser = new BridgeMdnsAdvertiser(id, name, server.Port);
        try { await advertiser.StartAsync(); return new(server, advertiser); }
        catch { await advertiser.DisposeAsync(); await server.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        await Advertiser.DisposeAsync();
        await Server.DisposeAsync();
    }
}
