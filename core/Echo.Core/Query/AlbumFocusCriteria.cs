using System.Text.Json;
using Echo.Core.Protocol;

namespace Echo.Core.Query;

internal sealed record FocusCriterion(string Field, object Value, bool Exclude = false);

internal static class AlbumFocusCriteria
{
    internal static readonly HashSet<string> AudioFormats = new(StringComparer.Ordinal)
    {
        "sampleRate44100", "sampleRate48000", "sampleRate88200", "sampleRate96000",
        "sampleRate176400", "sampleRate192000", "sampleRate352800", "sampleRate384000", "dsd", "otherSampleRate",
    };

    internal static List<FocusCriterion> Read(CoreAlbumFocus focus)
    {
        var values = new Dictionary<(string, object), FocusCriterion>();
        void Add(string field, object? value, bool exclude = false)
        {
            if (field is "decade" or "bitDepth")
            {
                if (value is not int number) return;
                if (field == "decade")
                {
                    if (number is < 1000 or > 2100) return;
                    value = number / 10 * 10;
                }
                else if (number != 24) return;
            }
            else
            {
                if (value is not string text || text.Length > 160 || string.IsNullOrWhiteSpace(text)) return;
                value = text.Trim();
                if (field == "genre" && (string)value == "__unclassified__") return;
                if (field == "audioFormat" && !AudioFormats.Contains((string)value)) return;
                if (field == "fileFormat" && (string)value != "flac") return;
                if (field is not ("genre" or "audioFormat" or "fileFormat" or "composer" or "conductor" or "performer")) return;
            }
            values[(field, value)] = new FocusCriterion(field, value, exclude);
        }
        Add("decade", focus.Decade); Add("genre", focus.GenreKey); Add("audioFormat", focus.AudioFormat);
        Add("bitDepth", focus.BitDepth); Add("fileFormat", focus.FileFormat);
        foreach (var item in (focus.Criteria ?? []).Take(32))
        {
            if (item is null) continue;
            object? value = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString()
                : item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt32(out var number) ? number : null;
            Add(item.Field, value, item.Exclude);
        }
        return values.Values.Take(32).ToList();
    }
}
