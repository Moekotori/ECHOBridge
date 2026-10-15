using System.Text.Json;
using Echo.Core.Protocol;

namespace Echo.Bridge.Library;

public sealed record BridgeProfile(string DatabasePath, string CoverDirectory, CoreSearchOptions SearchOptions)
{
    public static BridgeProfile Open(string path)
    {
        var database = Path.GetFullPath(path);
        if (!File.Exists(database)) throw new BridgeException(404, "library_not_found");
        var directory = Path.GetDirectoryName(database)!;
        var cover = Path.Combine(directory, "cover-cache");
        var search = new CoreSearchOptions();
        var settings = Path.Combine(directory, "echo-settings.json");
        if (File.Exists(settings) && new FileInfo(settings).Length <= 1024 * 1024)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(settings));
            var root = document.RootElement;
            if (root.TryGetProperty("coverCacheDir", out var configured) && configured.ValueKind == JsonValueKind.String
                && Path.IsPathFullyQualified(configured.GetString() ?? "")) cover = Path.GetFullPath(configured.GetString()!);
            if (root.TryGetProperty("chineseCrossScriptSearchEnabled", out var cross) && cross.ValueKind is JsonValueKind.True or JsonValueKind.False)
                search.ChineseCrossScriptSearchEnabled = cross.GetBoolean();
            if (root.TryGetProperty("artistMergeStrategy", out var strategy) && strategy.ValueKind == JsonValueKind.String)
                search.ArtistMergeStrategy = strategy.GetString() ?? "standard";
        }
        return new(database, cover, search);
    }

    public static IReadOnlyList<string> Discover()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new[] { "ECHO", "ECHO NEXT", "echo-next", "echo-steam", "ECHO Steam" }
            .Select(name => Path.Combine(appData, name, "echo-library.sqlite"))
            .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
