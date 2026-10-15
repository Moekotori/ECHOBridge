using System.Security.Cryptography;
using System.Text.Json;

namespace Echo.Bridge.Windows.Updates;

internal sealed record BridgeUpdateFile(string Name, long Size, string Sha256);

internal sealed record BridgeUpdateManifest(Version Version, IReadOnlyList<BridgeUpdateFile> Files)
{
    internal const long MaxExpanded = 512L * 1024 * 1024;
    internal const int MaxFiles = 2048;
    internal const string FileName = "release-manifest.json";
    internal static bool IsHash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);

    internal static string Relative(string name)
    {
        name = name.Replace('\\', '/');
        if (name.Length is 0 or > 240 || name.StartsWith('/') || name.Split('/').Any(part =>
                part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.Contains(':') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Invalid Bridge package path.");
        if (name.Split('/').Any(part => part is ".git" or "misc" or "node_modules" || part.StartsWith(".env", StringComparison.OrdinalIgnoreCase))
            || name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(name).Equals("bridge-settings.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Private data in Bridge package.");
        return name;
    }

    public static BridgeUpdateManifest Parse(Stream stream)
    {
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        var version = BridgeReleaseClient.Normalize(Version.Parse(root.GetProperty("version").GetString()!));
        var files = new List<BridgeUpdateFile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long size = 0;
        foreach (var row in root.GetProperty("files").EnumerateArray())
        {
            var name = Relative(row.GetProperty("name").GetString()!);
            var length = row.GetProperty("size").GetInt64();
            var hash = row.GetProperty("sha256").GetString()!;
            size += length;
            if (files.Count >= MaxFiles || !names.Add(name) || name.Equals(FileName, StringComparison.OrdinalIgnoreCase)
                || length < 0 || length > MaxExpanded || size > MaxExpanded || !IsHash(hash))
                throw new InvalidDataException("Invalid Bridge file manifest.");
            files.Add(new(name, length, hash));
        }
        foreach (var required in new[] { "ECHO Bridge.exe", "ECHO Bridge.dll", "Echo.Bridge.dll", "Echo.Core.dll", "coreclr.dll",
                     "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "System.Windows.Forms.dll", "e_sqlite3.dll" })
            if (!names.Contains(required)) throw new InvalidDataException("Incomplete Bridge runtime.");
        return new(version, files);
    }

    public static BridgeUpdateManifest Validate(string directory, Version expected)
    {
        var file = Path.Combine(directory, FileName);
        if (new FileInfo(file).Length > 1024 * 1024) throw new InvalidDataException("Bridge manifest exceeds its limit.");
        using var stream = File.OpenRead(file);
        var manifest = Parse(stream);
        if (manifest.Version != expected) throw new InvalidDataException("Bridge version mismatch.");
        foreach (var item in manifest.Files)
        {
            var path = Path.Combine(directory, item.Name);
            if (new FileInfo(path).Length != item.Size || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Bridge file size/type mismatch.");
            using var input = File.OpenRead(path);
            if (!Convert.ToHexStringLower(SHA256.HashData(input)).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Bridge file hash mismatch.");
        }
        return manifest;
    }
}
