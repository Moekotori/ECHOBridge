namespace Echo.Bridge.Library;

public static class BridgeArtwork
{
    public static object? Read(BridgeProfile profile, string id, CancellationToken cancellation)
    {
        if (id.Length is 0 or > 2048) throw new BridgeException(400, "invalid_peer_asset");
        var path = BridgeLibraryMetadata.Cover(profile, id, cancellation);
        if (path is null) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > 256 * 1024) throw new BridgeException(413, "peer_thumbnail_too_large");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        cancellation.ThrowIfCancellationRequested();
        var mime = Mime(bytes);
        return mime is null ? null : new { mime, bytes = Convert.ToBase64String(bytes) };
    }
    private static string? Mime(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}
