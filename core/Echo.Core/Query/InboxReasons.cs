using System.Text.Json;

namespace Echo.Core.Query;

internal sealed record InboxItemEvidence(
    string BatchId, string AddedAt, string Status, string TrackId,
    string? CoverId, string? CoverSource, string? MetadataStatus,
    string? EmbeddedMetadataStatus, string? EmbeddedCoverStatus,
    string Artist, string Album, string AlbumArtist, string? FieldSourcesJson,
    double Duration, long SizeBytes, string? Codec);

internal static class InboxReasons
{
    public static List<string> For(InboxItemEvidence row)
    {
        var reasons = new List<string>(9);
        if (string.IsNullOrWhiteSpace(row.CoverId) || string.IsNullOrWhiteSpace(row.CoverSource)
            || row.CoverSource == "default") reasons.Add("missing_cover");
        if (row.MetadataStatus == "fallback") reasons.Add("metadata_fallback");
        if (HasFilenameFallback(row.FieldSourcesJson)) reasons.Add("filename_fallback");
        var artist = row.Artist.Trim().ToLowerInvariant();
        var album = row.Album.Trim().ToLowerInvariant();
        var albumArtist = row.AlbumArtist.Trim().ToLowerInvariant();
        if (artist is "" or "unknown" or "unknown artist") reasons.Add("unknown_artist");
        if (album.Length == 0) reasons.Add("missing_album");
        else if (album is "unknown" or "unknown album") reasons.Add("unknown_album");
        if (albumArtist is "" or "unknown" or "unknown artist" or "unknown album") reasons.Add("missing_album_artist");
        if (row.EmbeddedMetadataStatus == "error") reasons.Add("embedded_metadata_error");
        if (row.EmbeddedCoverStatus == "error") reasons.Add("embedded_cover_error");
        if (row.Duration <= 0 || row.SizeBytes <= 0 || string.IsNullOrWhiteSpace(row.Codec)) reasons.Add("suspicious_file");
        return reasons;
    }

    private static bool HasFilenameFallback(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.EnumerateObject().Any(property => property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() == "filename_fallback");
        }
        catch (JsonException) { return false; }
    }
}
