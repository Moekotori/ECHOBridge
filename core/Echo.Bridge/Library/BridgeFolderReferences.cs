using Echo.Core.Protocol;

namespace Echo.Bridge.Library;

/// <summary>Opaque, bounded LAN references; source paths never leave the host.</summary>
public sealed class BridgeFolderReferences
{
    private sealed record Reference(string FolderId, string Path, DateTimeOffset Expires);
    private readonly Dictionary<string, Reference> references = [];
    public void Clear() => references.Clear();
    public (string FolderId, string? Path) Resolve(string id)
    {
        if (id.StartsWith("root:", StringComparison.Ordinal) && id.Length > 5) return (id[5..], null);
        if (!references.TryGetValue(id, out var value) || value.Expires <= DateTimeOffset.UtcNow)
            throw new BridgeException(404, "peer_folder_expired");
        references[id] = value with { Expires = DateTimeOffset.UtcNow.AddHours(24) };
        return (value.FolderId, value.Path);
    }
    public string Remember(LibraryFolderNodeDto node)
    {
        foreach (var key in references.Where(pair => pair.Value.Expires <= DateTimeOffset.UtcNow).Select(pair => pair.Key).ToArray())
            references.Remove(key);
        foreach (var pair in references)
            if (pair.Value.FolderId == node.FolderId && pair.Value.Path == node.Path) return pair.Key;
        if (references.Count >= 512) references.Remove(references.MinBy(pair => pair.Value.Expires).Key);
        var id = "node:" + Guid.NewGuid().ToString("N");
        references[id] = new(node.FolderId, node.Path, DateTimeOffset.UtcNow.AddHours(24));
        return id;
    }
}
