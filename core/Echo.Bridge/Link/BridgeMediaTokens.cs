using System.Net;
using System.Security.Cryptography;
using Echo.Core.Protocol;
using Echo.Bridge.Library;

namespace Echo.Bridge.Link;

public sealed class BridgeMediaTokens
{
    private sealed record Entry(string Path, IPAddress Client, DateTimeOffset Expires);
    private readonly Dictionary<string, Entry> tokens = [];
    private readonly object gate = new();
    public string Issue(LibraryTrackDto track, IPAddress client)
    {
        if (!BridgeTrackProjection.CanPlay(track) || !File.Exists(track.Path)) throw new BridgeException(415, "unsupported_format");
        lock (gate)
        {
            Prune();
            if (tokens.Count >= 256) tokens.Remove(tokens.MinBy(pair => pair.Value.Expires).Key);
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            tokens[token] = new(track.Path, client, DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(track.Duration * 2 + 600, 1800, 86400)));
            return token;
        }
    }
    public string Resolve(string token, IPAddress client)
    {
        lock (gate)
        {
            Prune();
            return token.Length == 64 && tokens.TryGetValue(token, out var entry) && entry.Client.Equals(client)
                ? entry.Path : throw new BridgeException(401, "media_token_expired_or_missing");
        }
    }
    public void Clear() { lock (gate) tokens.Clear(); }
    private void Prune()
    {
        foreach (var key in tokens.Where(pair => pair.Value.Expires <= DateTimeOffset.UtcNow).Select(pair => pair.Key).ToArray()) tokens.Remove(key);
    }
}
