using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Echo.Core.Export;

/// <summary>One-shot streaming export. No SQLite writer, network, or resident worker.</summary>
public static class DataPackageArchive
{
  public static async Task RunAsync(Stream input, Stream output)
  {
    // Metadata only. Binary assets are copied using one reusable 64 KiB buffer.
    using var reader = new StreamReader(input, Encoding.UTF8);
    var chars = new char[1024 * 1024]; var used = 0;
    while (used < chars.Length) { var n = await reader.ReadAsync(chars.AsMemory(used)); if (n == 0) break; used += n; }
    if (used == chars.Length) throw new InvalidOperationException("Export metadata limit exceeded.");
    using var doc = JsonDocument.Parse(new string(chars, 0, used));
    var root = doc.RootElement;
    var destination = Path.GetFullPath(root.GetProperty("outputPath").GetString()!);
    var temporary = Path.GetFullPath(root.GetProperty("temporaryPath").GetString()!);
    if (Path.GetDirectoryName(temporary) != Path.GetDirectoryName(destination) || temporary == destination || !temporary.EndsWith(".tmp", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid staging path.");
    var included = new HashSet<string>(StringComparer.Ordinal); var skipped = new List<string>();
    var metadataBytes = 0;
    void Account(string name)
    {
      metadataBytes += Encoding.UTF8.GetByteCount(name) + 32;
      if (metadataBytes > 256 * 1024) throw new InvalidOperationException("Archive inventory limit exceeded.");
    }
    void Skip(string name) { Account(name); skipped.Add(name); }
    var buffer = new byte[64 * 1024];
    try
    {
      using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
      using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
      {
        string Register(string name)
        {
          name = name.Replace('\\', '/');
          if (name.StartsWith('/') || name.Split('/').Any(p => p is ".." or "." or "") || name.Contains(':')) throw new InvalidOperationException("Invalid archive entry.");
          if (included.Count >= 20000 || !included.Add(name)) throw new InvalidOperationException("Archive entry limit or duplicate entry.");
          Account(name);
          return name;
        }
        async Task AddFile(string name, string path)
        {
          FileStream source;
          try { source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan); }
          catch (FileNotFoundException) { Skip(name); return; }
          catch (DirectoryNotFoundException) { Skip(name); return; }
          await using (source)
          await using (var target = zip.CreateEntry(Register(name), CompressionLevel.Optimal).Open())
          {
            int n; while ((n = await source.ReadAsync(buffer)) > 0) await target.WriteAsync(buffer.AsMemory(0, n));
          }
        }
        async Task AddDirectory(string prefix, string path, int depth)
        {
          if (!Directory.Exists(path)) return;
          if (depth > 32) throw new InvalidOperationException("Archive directory depth exceeded.");
          if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { Skip(prefix); return; }
          foreach (var child in Directory.EnumerateFileSystemEntries(path))
          {
            var name = prefix + "/" + Path.GetFileName(child);
            var attributes = File.GetAttributes(child);
            if ((attributes & FileAttributes.ReparsePoint) != 0) { Skip(name); continue; }
            if ((attributes & FileAttributes.Directory) != 0) await AddDirectory(name, child, depth + 1);
            else await AddFile(name, child);
          }
        }
        foreach (var entry in root.GetProperty("entries").EnumerateArray())
        {
          var name = entry.GetProperty("name").GetString()!;
          if (entry.TryGetProperty("path", out var path)) await AddFile(name, path.GetString()!);
          else
          {
            await using var target = zip.CreateEntry(Register(name), CompressionLevel.Optimal).Open();
            await using var writer = new StreamWriter(target, new UTF8Encoding(false));
            await writer.WriteAsync(entry.GetProperty("text").GetString());
          }
        }
        foreach (var dir in root.GetProperty("directories").EnumerateArray()) await AddDirectory(dir.GetProperty("name").GetString()!, dir.GetProperty("path").GetString()!, 0);
      }
      var receipt = JsonSerializer.SerializeToUtf8Bytes(new { includedEntries = included.Order(StringComparer.Ordinal).ToArray(), skippedEntries = skipped });
      if (receipt.Length > 1024 * 1024) throw new InvalidOperationException("Archive receipt limit exceeded.");
      File.Move(temporary, destination, true);
      await output.WriteAsync(receipt);
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
  }
}
