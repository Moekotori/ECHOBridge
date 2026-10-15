using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Echo.Bridge.Windows.Updates;

internal sealed record BridgeRelease(Version Version, Uri Download, long Size, string Sha256);

/// <summary>Anonymous, bounded access to this application's published GitHub assets.</summary>
internal sealed class BridgeReleaseClient(HttpClient http, Version current)
{
    internal const long MaxDownload = 256L * 1024 * 1024;
    internal const string Repository = "https://github.com/Moekotori/ECHOBridge";
    private static readonly Uri Releases = new("https://api.github.com/repos/Moekotori/ECHOBridge/releases?per_page=20");
    internal static Version Normalize(Version version) => new(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<BridgeRelease?> Check(CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await Get(Releases, "application/vnd.github+json", deadline.Token).ConfigureAwait(false);
        using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        await Copy(body, output, 1024 * 1024, deadline.Token).ConfigureAwait(false);
        using var json = JsonDocument.Parse(output.GetBuffer().AsMemory(0, (int)output.Length));
        return Select(json.RootElement, current);
    }

    internal static BridgeRelease? Select(JsonElement releases, Version current)
    {
        BridgeRelease? selected = null;
        foreach (var release in releases.EnumerateArray().Take(20))
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString()!;
            if (!Regex.IsMatch(tag, @"^v\d{1,5}\.\d{1,5}\.\d{1,5}(?:\.\d{1,5})?$", RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(20)) || !Version.TryParse(tag[1..], out var version)) continue;
            version = Normalize(version);
            if (version <= current || (selected is not null && version <= selected.Version)) continue;
            var expected = $"ECHO-Bridge-{tag[1..]}-win-x64-unsigned-test.zip";
            var standard = $"ECHO-Bridge-{tag[1..]}-win-x64.zip";
            foreach (var asset in release.GetProperty("assets").EnumerateArray().OrderBy(asset => asset.GetProperty("name").GetString() == standard ? 0 : 1))
            {
                var name = asset.GetProperty("name").GetString();
                if (name != expected && name != standard || asset.GetProperty("state").GetString() != "uploaded") continue;
                var size = asset.GetProperty("size").GetInt64();
                var digest = asset.GetProperty("digest").GetString();
                if (size is <= 0 or > MaxDownload || digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal)
                    || !BridgeUpdateManifest.IsHash(digest[7..])) continue;
                var download = new Uri(asset.GetProperty("browser_download_url").GetString()!);
                if (download.AbsoluteUri != $"{Repository}/releases/download/{tag}/{name}")
                    throw new InvalidDataException("Unexpected Bridge release origin.");
                selected = new(version, download, size, digest[7..].ToLowerInvariant());
                break;
            }
        }
        return selected;
    }

    public async Task Download(BridgeRelease release, string file, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var created = false;
        try
        {
            using var response = await Get(release.Download, "application/octet-stream", deadline.Token).ConfigureAwait(false);
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                created = true;
                await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                await Copy(body, output, release.Size, deadline.Token).ConfigureAwait(false);
                if (output.Length != release.Size) throw new InvalidDataException("Incomplete Bridge download.");
            }
            await using var input = File.OpenRead(file);
            var hash = await SHA256.HashDataAsync(input, deadline.Token).ConfigureAwait(false);
            if (!Convert.ToHexStringLower(hash).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Bridge download hash mismatch.");
        }
        catch { if (created && File.Exists(file)) File.Delete(file); throw; }
    }

    private async Task<HttpResponseMessage> Get(Uri uri, string accept, CancellationToken cancellation)
    {
        for (var redirect = 0; redirect <= 3; redirect++)
        {
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
                || uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com"))
                throw new InvalidDataException("Unexpected Bridge download redirect.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("ECHO-Bridge/" + current);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location is null) throw new InvalidDataException("Missing Bridge redirect.");
                uri = new Uri(uri, location); continue;
            }
            if (!response.IsSuccessStatusCode) { response.Dispose(); throw new HttpRequestException("Bridge Release unavailable."); }
            return response;
        }
        throw new InvalidDataException("Too many Bridge redirects.");
    }

    private static async Task Copy(Stream source, Stream target, long limit, CancellationToken cancellation)
    {
        var buffer = new byte[65536]; long total = 0;
        for (var count = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false); count != 0;
             count = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false))
        {
            total += count;
            if (total > limit) throw new InvalidDataException("Bridge response exceeds its limit.");
            await target.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false);
        }
    }
}
