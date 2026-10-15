using System.Text.Json;

namespace Echo.Bridge.Windows.Updates;

internal static class BridgeUpdateSmoke
{
    public static async Task Download(string directory)
    {
        directory = BridgeSmoke.Evidence(directory);
        try
        {
            using var http = BridgeReleaseClient.CreateHttpClient();
            // Exercise a real existing release as an upgrade from a synthetic earlier version.
            var client = new BridgeReleaseClient(http, new Version(0, 0, 0, 0));
            var release = await client.Check(default) ?? throw new InvalidDataException("No published Bridge asset.");
            var archive = Path.Combine(directory, "update.zip");
            await client.Download(release, archive, default);
            await Task.Run(() => BridgeUpdatePackage.Extract(archive, Path.Combine(directory, "package"), release));
            File.WriteAllBytes(Path.Combine(directory, "download.json"), JsonSerializer.SerializeToUtf8Bytes(new
                { ok = true, version = release.Version.ToString(), bytes = release.Size, hashVerified = true, manifestVerified = true }));
        }
        catch (Exception error) when (error is IOException or HttpRequestException or JsonException or OperationCanceledException or FormatException or KeyNotFoundException)
        {
            Environment.ExitCode = 1; File.WriteAllText(Path.Combine(directory, "download.json"), "{\"ok\":false}");
        }
    }
}
