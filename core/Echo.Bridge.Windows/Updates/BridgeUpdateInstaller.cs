namespace Echo.Bridge.Windows.Updates;

/// <summary>Only manifest-owned files move; partial file replacement rolls back before propagating failure.</summary>
internal sealed class BridgeUpdateInstaller : IDisposable
{
    private readonly string source, target;
    private readonly List<string> moved = [], installed = [];
    private bool committed;
    public string Backup { get; }

    public BridgeUpdateInstaller(string source, string target, Version oldVersion, Version newVersion)
    {
        this.source = Path.GetFullPath(source); this.target = Path.GetFullPath(target);
        var prefix = this.source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (newVersion <= oldVersion || this.target.Equals(this.source, StringComparison.OrdinalIgnoreCase)
            || this.target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || this.source.StartsWith(this.target.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bridge source and target overlap.");
        var old = BridgeUpdateManifest.Validate(this.target, oldVersion);
        var next = BridgeUpdateManifest.Validate(this.source, newVersion);
        var previous = old.Files.Select(file => file.Name).Append(BridgeUpdateManifest.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incoming = next.Files.Select(file => file.Name).Append(BridgeUpdateManifest.FileName).ToArray();
        foreach (var name in incoming)
            if (!previous.Contains(name) && File.Exists(Path.Combine(this.target, name)))
                throw new IOException("Bridge update would replace an unmanaged file.");
        Backup = Path.Combine(this.target, ".echo-bridge-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Backup);
        try
        {
            foreach (var name in previous)
            {
                var backup = Path.Combine(Backup, name); Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(Path.Combine(this.target, name), backup); moved.Add(name);
            }
            foreach (var name in incoming)
            {
                var output = Path.Combine(this.target, name); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(Path.Combine(this.source, name), output, overwrite: false); installed.Add(name);
            }
        }
        catch { Rollback(); throw; }
    }

    public void Commit() => committed = true;

    private void Rollback()
    {
        foreach (var name in installed.AsEnumerable().Reverse()) File.Delete(Path.Combine(target, name));
        foreach (var name in moved.AsEnumerable().Reverse()) File.Move(Path.Combine(Backup, name), Path.Combine(target, name));
        // Backup is generated below the verified application directory, never an externally supplied path.
        Directory.Delete(Backup, recursive: true); installed.Clear(); moved.Clear();
    }

    public void Dispose() { if (!committed) Rollback(); }
}
