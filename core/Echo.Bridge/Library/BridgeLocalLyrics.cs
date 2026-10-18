using System.Globalization;
using System.Text.RegularExpressions;

namespace Echo.Bridge.Library;

internal static class BridgeLocalLyrics
{
    private sealed record Line(long timeMs, string text) { public long? endMs => null; }
    private static readonly Regex Offset = Pattern(@"^\s*\[offset:\s*([+-]?[0-9]+)\s*\]\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex Metadata = Pattern(@"^\s*\[(ar|ti|al|by|offset|length|re|ve):[^\]]*\]\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex Prefix = Pattern(@"^\s*(?<timestamps>(?:\[[0-9]{1,3}:[0-9]{2}(?:\.[0-9]{1,3})?\])+)(?<text>.*)$");
    private static readonly Regex Timestamp = Pattern(@"\[([0-9]{1,3}):([0-9]{2})(?:\.([0-9]{1,3}))?\]");
    private static Regex Pattern(string pattern, RegexOptions options = RegexOptions.None) => new(pattern, options | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(20));

    internal static Dictionary<string, object?> Parse(string text)
    {
        if (text.Length > BridgeLyricProjection.MaxCharacters) throw BridgeLyricProjection.TooLarge();
        var offsetMatch = Offset.Match(text);
        var offset = offsetMatch.Success && long.TryParse(offsetMatch.Groups[1].Value, NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var parsedOffset) ? Math.Clamp(parsedOffset, -600_000L, 600_000L) : 0;
        var synced = new List<Line>(); var plain = new List<Line>(); var characters = 0;
        void Add(List<Line> target, long time, string content)
        {
            characters += content.Length;
            if (content.Length > 4096 || characters > BridgeLyricProjection.MaxCharacters
                || synced.Count + plain.Count >= BridgeLyricProjection.MaxLines) throw BridgeLyricProjection.TooLarge();
            target.Add(new(time, content));
        }
        using var source = new StringReader(text);
        while (source.ReadLine() is { } line)
        {
            if (Metadata.IsMatch(line)) continue;
            var match = Prefix.Match(line);
            if (!match.Success)
            {
                if (!string.IsNullOrWhiteSpace(line)) Add(plain, -1, line.Trim());
                continue;
            }
            var content = match.Groups["text"].Value.TrimEnd();
            for (var stamp = Timestamp.Match(match.Groups["timestamps"].Value); stamp.Success; stamp = stamp.NextMatch())
            {
                var milliseconds = (long.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                    + long.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture)) * 1000;
                if (stamp.Groups[3].Success) milliseconds += long.Parse(stamp.Groups[3].Value.PadRight(3, '0'), CultureInfo.InvariantCulture);
                Add(synced, Math.Max(0, milliseconds - offset), content);
            }
        }
        var timed = synced.Count > 0;
        return new() { ["kind"] = timed ? "synced" : "plain", ["lines"] = timed ? synced.OrderBy(line => line.timeMs).ToArray() : plain.ToArray(),
            ["plainText"] = timed ? null : text, ["syncedText"] = timed ? text : null, ["offsetMs"] = 0 };
    }
}
