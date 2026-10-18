using Echo.Core.Media;
using Echo.Core.Protocol;

namespace Echo.Bridge.Library;

/// <summary>One catalog page owns at most eight parsed CUE sheets; no media or catalog is retained afterward.</summary>
internal sealed class BridgeTrackSources
{
    private readonly Dictionary<string, ScanCueResult> sheets = [];
    internal BridgeTrackSource Resolve(LibraryTrackDto track, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!BridgeTrackProjection.SupportsTrack(track)) throw new BridgeException(415, "unsupported_format");
        var marker = track.Path.LastIndexOf("#cueTrack=", StringComparison.Ordinal);
        if (marker < 0)
        {
            if (!File.Exists(track.Path)) throw new BridgeException(404, "peer_track_not_found");
            return new(track.Path, null);
        }
        if (!int.TryParse(track.Path[(marker + 10)..], out var number)) throw new BridgeException(404, "peer_track_not_found");
        var path = track.Path[..marker];
        if (!sheets.TryGetValue(path, out var result))
        {
            result = ScanCueReader.Read(new() { FilePath = path, Embedded = !Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase),
                IsolateFileErrors = true }, cancellation);
            if (sheets.Count < 8) sheets.Add(path, result);
        }
        var cue = result.Tracks.FirstOrDefault(row => row.TrackNumber == number);
        if (cue?.CueTrack is not { } details || !File.Exists(cue.AudioPath)) throw new BridgeException(404, "peer_track_not_found");
        if (!BridgeTrackProjection.SupportsPath(cue.AudioPath)) throw new BridgeException(415, "unsupported_format");
        return new(cue.AudioPath, new(details.StartSeconds, details.EndSeconds));
    }
    internal BridgeTrackSource? Playable(LibraryTrackListRowDto row, CancellationToken cancellation)
    {
        if (row is not LibraryTrackDto track) return null;
        try { return Resolve(track, cancellation); }
        catch (BridgeException) { return null; }
    }
}
