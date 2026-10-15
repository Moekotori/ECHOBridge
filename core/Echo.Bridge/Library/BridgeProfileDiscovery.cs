using System.Text.Json;

namespace Echo.Bridge.Library;

/// <summary>Only bounded local hints and standard profile directories; never scans media.</summary>
public static class BridgeProfileDiscovery
{
    public static string? Automatic(string? appData = null)
    {
        appData ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var hint = Path.Combine(appData, "ECHO Bridge", "echo-library-profile.json");
        try
        {
            if (File.Exists(hint) && new FileInfo(hint).Length <= 16 * 1024)
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(hint));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var value) && value == 1
                    && root.TryGetProperty("databasePath", out var database) && database.ValueKind == JsonValueKind.String
                    && database.GetString() is { Length: > 0 and <= 4096 } path
                    && Path.IsPathFullyQualified(path) && File.Exists(path)) return Path.GetFullPath(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        // Match ECHO's regular-edition preference before considering legacy Steam-only folders.
        var regular = new[] { "ECHO", "ECHO NEXT", "echo-next" };
        return Candidates(appData, regular).OrderByDescending(path => Score(Path.GetDirectoryName(path)!)).FirstOrDefault()
            ?? Candidates(appData, ["echo-steam", "ECHO Steam"]).FirstOrDefault();
    }

    private static IEnumerable<string> Candidates(string appData, string[] names) => names
        .Select(name => Path.Combine(appData, name, "echo-library.sqlite"))
        .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

    private static long Score(string directory)
    {
        long score = 0;
        foreach (var (file, weight) in new[] { ("echo-library.sqlite", 10000), ("echo-settings.json", 1000) })
        {
            try { score += weight + Math.Min(999, new FileInfo(Path.Combine(directory, file)).Length / (1024 * 1024)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return score;
    }
}
