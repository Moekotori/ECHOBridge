using Echo.Core.Protocol;

namespace Echo.Bridge.Library;

public static class BridgeTrackProjection
{
    private static readonly HashSet<string> Extensions = [".mp3", ".flac", ".wav", ".m4a", ".mp4", ".aac",
        ".ogg", ".opus", ".aif", ".aiff", ".alac", ".ape", ".wv", ".tta", ".wma"];

    public static bool CanPlay(LibraryTrackListRowDto row) => row is LibraryTrackDto track
        && track.MediaType == "local" && !track.Path.Contains("#cueTrack=", StringComparison.Ordinal)
        && Extensions.Contains(Path.GetExtension(track.Path).ToLowerInvariant()) && !track.Unavailable;

    public static Dictionary<string, object?> Entry(LibraryTrackListRowDto row) => new()
    {
        ["kind"] = "track", ["id"] = row.Id, ["title"] = Text(row.Title),
        ["subtitle"] = Text(string.Join(" · ", new[] { row.Artist, row.Album }.Where(s => s.Length != 0))),
        ["count"] = 0, ["coverId"] = row.CoverId, ["artist"] = Text(row.Artist), ["album"] = Text(row.Album),
        ["durationMs"] = Duration(row.Duration), ["canPlay"] = CanPlay(row), ["codec"] = row.Codec,
        ["sampleRate"] = row.SampleRate, ["bitDepth"] = row.BitDepth, ["bitrate"] = row.Bitrate,
        ["bpm"] = row.Bpm, ["bpmConfidence"] = row.BpmConfidence, ["analysisStatus"] = row.AnalysisStatus,
        ["bpmEstimated"] = row.FieldSources?.GetValueOrDefault("bpm") == "audio_analysis",
        ["mqa"] = row.Mqa, ["vocalPresence"] = row.VocalPresence,
    };

    public static Dictionary<string, object?> PlaylistEntry(LibraryPlaylistItemDto row)
    {
        var entry = row.Track is not null ? Entry(row.Track) : new Dictionary<string, object?>
        {
            ["kind"] = "track", ["id"] = row.MediaId ?? row.Id, ["title"] = Text(row.TitleSnapshot ?? "Unavailable track"),
            ["subtitle"] = Text(string.Join(" · ", row.ArtistSnapshot, row.AlbumSnapshot)), ["count"] = 0,
            ["coverId"] = row.CoverId, ["durationMs"] = Duration(row.DurationSnapshot ?? 0), ["canPlay"] = false,
        };
        entry["playlistItemId"] = row.Id;
        if (row.Unavailable) entry["canPlay"] = false;
        return entry;
    }

    public static string Text(string text) => text.Length > 2048 ? text[..2048] : text;
    public static long Duration(double seconds) => double.IsFinite(seconds) ? (long)Math.Clamp(Math.Round(seconds * 1000), 0, 86_400_000_000) : 0;
    public static Dictionary<string, object?> Container(string kind, string id, string title, string subtitle, long count, string? coverId)
        => new() { ["kind"] = kind, ["id"] = id, ["title"] = Text(title), ["subtitle"] = Text(subtitle), ["count"] = Math.Max(0, count), ["coverId"] = coverId };
}
