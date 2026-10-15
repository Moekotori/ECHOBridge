using System.Net;
using System.Text.Json;
using Echo.Bridge.Library;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Echo.Bridge.Link;

/// <summary>A bounded, library-only implementation of the existing Echo Link peer protocol.</summary>
public sealed class BridgeHttpServer(BridgeLibraryReader reader, string deviceId, string name) : IAsyncDisposable
{
    private readonly BridgeCatalog catalog = new(reader);
    private readonly BridgeMediaTokens media = new();
    private readonly SemaphoreSlim requests = new(4, 4);
    private readonly SemaphoreSlim queries = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private WebApplication? application;
    public int Port { get; private set; }
    public string DeviceId { get; } = deviceId;
    public string Name { get; } = name;
    public int ActiveRequests => 4 - requests.CurrentCount;
    public object Device => new { id = DeviceId, name = Name };

    public async Task StartAsync(int preferredPort = 0)
    {
        try { application = await StartPort(preferredPort); }
        catch (IOException) when (preferredPort != 0) { application = await StartPort(0); }
        var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        Port = new Uri(addresses.Single()).Port;
    }
    private async Task<WebApplication> StartPort(int port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxConcurrentConnections = 16;
            options.Limits.MaxRequestBodySize = 0;
            options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            options.Limits.MaxRequestLineSize = 8 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(5);
            options.ListenAnyIP(port, endpoint => endpoint.Protocols = HttpProtocols.Http1);
        });
        var app = builder.Build();
        app.Run(Handle);
        try { await app.StartAsync(lifetime.Token); return app; }
        catch { await app.DisposeAsync(); throw; }
    }

    private async Task Handle(HttpContext context)
    {
        if (!requests.Wait(0)) { context.Response.StatusCode = 503; return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.Token);
        var path = context.Request.Path.Value ?? "";
        cancellation.CancelAfter(path.StartsWith("/echo-link/media/", StringComparison.Ordinal) ? TimeSpan.FromHours(24) : TimeSpan.FromSeconds(8));
        try
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            var client = context.Connection.RemoteIpAddress;
            if (!BridgeLan.IsAllowed(client)) throw new BridgeException(403, "lan_only");
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) throw new BridgeException(405, "library_read_only");
            var headers = context.Request.Headers;
            if (headers.ContainsKey("Origin") || headers.ContainsKey("Cookie") || headers.ContainsKey("Authorization")
                || headers.Keys.Any(key => key.StartsWith("Sec-Fetch-", StringComparison.OrdinalIgnoreCase)))
                throw new BridgeException(401, "unauthorized");
            if (path.StartsWith("/echo-link/media/", StringComparison.Ordinal))
            {
                await BridgeMediaResponse.Send(context, media.Resolve(path[17..], client!.MapToIPv4()), cancellation.Token);
                return;
            }
            if (headers["X-ECHO-Link-Direct"] != "1" || headers["X-ECHO-Link-Version"] != "1") throw new BridgeException(401, "unauthorized");
            await queries.WaitAsync(cancellation.Token);
            object result;
            try { result = await Task.Run(() => Query(context, path, client!.MapToIPv4(), cancellation.Token), cancellation.Token); }
            finally { queries.Release(); }
            await Json(context, result, cancellation.Token);
        }
        catch (OperationCanceledException) { if (context.Response.HasStarted) context.Abort(); else context.Response.StatusCode = 504; }
        catch (BridgeException error) { await Error(context, error.Status, error.Message); }
        catch (Echo.Core.Query.UnsupportedQueryException) { await Error(context, 422, "unsupported_peer_query"); }
        catch (Exception) { await Error(context, 503, "library_read_failed"); }
        finally { requests.Release(); }
    }

    private object Query(HttpContext context, string path, IPAddress client, CancellationToken cancellation)
    {
        var query = context.Request.Query;
        string Read(string key) => query[key].ToString();
        if (path == "/echo-link/peer/v1/device") return new { device = Device, version = 1, capabilities = new { library = true, playbackControl = false } };
        if (path == "/echo-link/peer/v1/catalog")
        {
            var page = Read("page");
            if (!int.TryParse(page.Length == 0 ? "1" : page, out var number)) throw new BridgeException(400, "invalid_peer_catalog_query");
            var sort = Read("sort"); var parentKind = Read("parentKind");
            return new { device = Device, catalog = catalog.Page(Read("kind") is "" ? "songs" : Read("kind"), number, Read("q"),
                sort.Length == 0 ? null : sort, parentKind.Length == 0 ? null : parentKind, Read("parentId"), cancellation) };
        }
        if (path == "/echo-link/peer/v1/thumb") return new { device = Device, image = BridgeArtwork.Read(reader.Profile, Read("id"), cancellation) };
        if (path == "/echo-link/peer/v1/lyrics") return new { device = Device, lyrics = BridgeLyrics.Read(reader, Read("id"), cancellation) };
        if (path == "/echo-link/peer/v1/stream")
        {
            var track = reader.Track(Read("id"), cancellation);
            var token = media.Issue(track, client);
            var local = context.Connection.LocalIpAddress!.MapToIPv4();
            return new { device = Device, track = new { id = track.Id, title = BridgeTrackProjection.Text(track.Title),
                artist = BridgeTrackProjection.Text(track.Artist), album = BridgeTrackProjection.Text(track.Album),
                durationMs = BridgeTrackProjection.Duration(track.Duration), canPlay = true,
                streamUrl = $"http://{local}:{Port}/echo-link/media/{token}", artworkUrl = (string?)null,
                audio = new { codec = track.Codec, sampleRateHz = track.SampleRate, bitDepth = track.BitDepth } } };
        }
        throw new BridgeException(404, "peer_endpoint_not_found");
    }

    private static async Task Json(HttpContext context, object value, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 512 * 1024) throw new BridgeException(413, "peer_response_too_large");
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.Body.WriteAsync(bytes, cancellation);
    }
    private static async Task Error(HttpContext context, int status, string code)
    {
        if (context.Response.HasStarted) { context.Abort(); return; }
        context.Response.StatusCode = status;
        try { await Json(context, new { error = new { code } }, context.RequestAborted); }
        catch (OperationCanceledException) { context.Abort(); }
    }
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        if (application is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await application.StopAsync(timeout.Token);
            await application.DisposeAsync();
        }
        catalog.Clear(); media.Clear();
        lifetime.Dispose();
    }
}
