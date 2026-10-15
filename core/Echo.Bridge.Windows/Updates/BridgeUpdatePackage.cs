using System.IO.Compression;
using System.Security.Cryptography;

namespace Echo.Bridge.Windows.Updates;

internal static class BridgeUpdatePackage
{
    public static void Extract(string archive, string destination, BridgeRelease release)
    {
        if (Directory.Exists(destination)) throw new IOException("Bridge staging directory already exists.");
        using (var file = File.OpenRead(archive))
            if (file.Length != release.Size || !Convert.ToHexStringLower(SHA256.HashData(file)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Bridge archive hash mismatch.");
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > BridgeUpdateManifest.MaxFiles) throw new InvalidDataException("Too many Bridge archive entries.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = BridgeUpdateManifest.Relative(entry.FullName.TrimEnd('/'));
            total += entry.Length;
            if (!names.Add(name) || entry.Length < 0 || entry.Length > BridgeUpdateManifest.MaxExpanded
                || total > BridgeUpdateManifest.MaxExpanded || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Invalid Bridge archive entry.");
        }
        var manifestEntry = zip.GetEntry(BridgeUpdateManifest.FileName) ?? throw new InvalidDataException("Bridge manifest missing.");
        if (manifestEntry.Length > 1024 * 1024) throw new InvalidDataException("Bridge manifest exceeds its limit.");
        BridgeUpdateManifest manifest;
        using (var stream = manifestEntry.Open()) manifest = BridgeUpdateManifest.Parse(stream);
        if (manifest.Version != release.Version) throw new InvalidDataException("Bridge release version mismatch.");
        var expected = manifest.Files.Select(file => file.Name).Append(BridgeUpdateManifest.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = zip.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToArray();
        if (!expected.SetEquals(files.Select(entry => entry.FullName.Replace('\\', '/'))))
            throw new InvalidDataException("Bridge archive contains unlisted files.");
        Directory.CreateDirectory(destination);
        foreach (var entry in files)
        {
            var target = Path.Combine(destination, BridgeUpdateManifest.Relative(entry.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
        BridgeUpdateManifest.Validate(destination, release.Version);
    }
}
