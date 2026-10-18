using Echo.Core.Protocol;

namespace Echo.Bridge.Library;

public static class BridgeTrackProjection
{
    private static readonly HashSet<string> Extensions = [".mp3", ".flac", ".wav", ".m4a", ".mp4", ".aac",
        ".ogg", ".opus", ".aif", ".aiff", ".alac", ".ape", ".wv", ".tta", ".wma"];

    public static bool SupportsPath(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    private static bool SupportsPhonePath(string path) => SupportsPath(path) && !Path.GetExtension(path).Equals(".tta", StringComparison.OrdinalIgnoreCase);
    public static bool CanPlay(LibraryTrackListRowDto row) => new BridgeTrackSources().Playable(row, default) is not null;
    internal static bool SupportsTrack(LibraryTrackListRowDto row)
    {
        if (row is not LibraryTrackDto track || track.MediaType != "local" || track.Unavailable) return false;
        var marker = track.Path.LastIndexOf("#cueTrack=", StringComparison.Ordinal);
        var path = marker < 0 ? track.Path : track.Path[..marker];
        if (track.Codec?.Contains("dsd", StringComparison.OrdinalIgnoreCase) == true || track.Codec?.ToLowerInvariant() is "dsf" or "dff") return false;
        return SupportsPath(path) || marker >= 0 && Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase);
    }

    public static Dictionary<string, object?> Entry(LibraryTrackListRowDto row) => Entry(row, new BridgeTrackSources(), default);
    internal static Dictionary<string, object?> Entry(LibraryTrackListRowDto row, BridgeTrackSources sources, CancellationToken cancellation)
        => Entry(row, sources.Playable(row, cancellation));
    internal static Dictionary<string, object?> Entry(LibraryTrackListRowDto row, BridgeTrackSource? source)
    {
        var entry = new Dictionary<string, object?>
    {
        ["kind"] = "track", ["id"] = row.Id, ["title"] = Text(row.Title),
        ["subtitle"] = Text(string.Join(" · ", new[] { row.Artist, row.Album }.Where(s => s.Length != 0))),
        ["count"] = 0, ["coverId"] = row.CoverId, ["artist"] = Text(row.Artist), ["album"] = Text(row.Album),
        ["durationMs"] = Duration(row.Duration), ["canPlay"] = source is not null, ["codec"] = row.Codec,
        ["canPlayOnPhone"] = source is not null && SupportsPhonePath(source.Path),
        ["sampleRate"] = row.SampleRate, ["bitDepth"] = row.BitDepth, ["bitrate"] = row.Bitrate,
        ["bpm"] = row.Bpm, ["bpmConfidence"] = row.BpmConfidence, ["analysisStatus"] = row.AnalysisStatus,
        ["bpmEstimated"] = row.FieldSources?.GetValueOrDefault("bpm") == "audio_analysis",
        ["mqa"] = row.Mqa, ["vocalPresence"] = row.VocalPresence,
        };
        if (source?.Range is not null) entry["sourceRange"] = source.Range;
        return entry;
    }

    public static Dictionary<string, object?> PlaylistEntry(LibraryPlaylistItemDto row)
        => PlaylistEntry(row, new BridgeTrackSources(), default);
    internal static Dictionary<string, object?> PlaylistEntry(LibraryPlaylistItemDto row, BridgeTrackSources sources, CancellationToken cancellation)
    {
        var entry = row.Track is not null ? Entry(row.Track, sources, cancellation) : new Dictionary<string, object?>
        {
            ["kind"] = "track", ["id"] = row.MediaId ?? row.Id, ["title"] = Text(row.TitleSnapshot ?? "Unavailable track"),
            ["subtitle"] = Text(string.Join(" · ", row.ArtistSnapshot, row.AlbumSnapshot)), ["count"] = 0,
            ["coverId"] = row.CoverId, ["durationMs"] = Duration(row.DurationSnapshot ?? 0), ["canPlay"] = false, ["canPlayOnPhone"] = false,
        };
        entry["playlistItemId"] = row.Id;
        if (row.Unavailable) { entry["canPlay"] = false; entry["canPlayOnPhone"] = false; }
        return entry;
    }

    public static string Text(string text) => text.Length > 2048 ? text[..2048] : text;
    public static long Duration(double seconds) => double.IsFinite(seconds) ? (long)Math.Clamp(Math.Round(seconds * 1000), 0, 86_400_000_000) : 0;
    public static Dictionary<string, object?> Container(string kind, string id, string title, string subtitle, long count, string? coverId)
        => new() { ["kind"] = kind, ["id"] = id, ["title"] = Text(title), ["subtitle"] = Text(subtitle), ["count"] = Math.Max(0, count), ["coverId"] = coverId };
}
