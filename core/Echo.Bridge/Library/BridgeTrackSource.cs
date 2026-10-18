using Echo.Core.Protocol;

namespace Echo.Bridge.Library;

public sealed record BridgeSourceRange(double startSeconds, double? endSeconds);
public sealed record BridgeTrackSource(string Path, BridgeSourceRange? Range)
{
    public static BridgeTrackSource Resolve(LibraryTrackDto track, CancellationToken cancellation = default)
        => new BridgeTrackSources().Resolve(track, cancellation);
}
