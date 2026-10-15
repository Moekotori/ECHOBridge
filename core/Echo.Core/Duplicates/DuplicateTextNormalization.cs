using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Echo.Core.Duplicates;

/// <summary>Matches the existing JavaScript strict-duplicate text normalization.</summary>
internal static class DuplicateTextNormalization
{
    // JavaScript \s includes FEFF but excludes NEL (U+0085), unlike .NET's \s.
    private static readonly Regex Whitespace = new(@"[\u0009-\u000D\u0020\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]+", RegexOptions.Compiled);
    private static readonly Regex EdgeQuotes = new("^[ \\\"'“”‘’「」『』《》]+|[ \\\"'“”‘’「」『』《》]+$", RegexOptions.Compiled);
    private static readonly Regex[] SourcePrefixes =
    [
        new(@"^【 *转载 *】 *"), new(@"^\[ *转载 *\] *"),
        new(@"^\( *转载 *\) *"), new(@"^转载[ :：-]*"), new(@"^搬运[ :：-]*"),
    ];
    private static readonly Regex[] VersionMarkers =
    [
        Marker(@"\blive\b"), Marker(@"\bremix\b"), Marker(@"\bremaster\b"), Marker(@"\bremastered\b"),
        Marker(@"\bcover\b"), Marker(@"\binstrumental\b"), Marker(@"\bkaraoke\b"), Marker(@"\boff +vocal\b"),
        Marker(@"\btv +size\b"), Marker(@"\bshort +ver\b"), Marker(@"\blong +ver\b"),
        Marker(@"\bradio +edit\b"), Marker(@"\bextended\b"), Marker(@"\bacoustic\b"), Marker(@"\bdemo\b"),
        Marker(@"\bmono\b"), Marker(@"\bstereo +mix\b"),
        new("现场"), new("翻唱"), new("伴奏"), new("重制"), new("剪辑版"), new("完整版"), new("加长版"), new("纯音乐"),
    ];
    private static readonly Regex SpeedVersion = new(@"(?:^|[\s._()[\]-])(0\.\d+|1\.\d+|2\.\d+)\s*[x×]?(?=$|[\s._()[\]-])", RegexOptions.Compiled);

    public static string Normalize(string value) => Whitespace.Replace(JsLower(value.Normalize(NormalizationForm.FormKC)), " ").Trim(' ');

    public static string Title(string value)
    {
        var title = EdgeQuotes.Replace(Normalize(value), "").Trim(' ');
        foreach (var prefix in SourcePrefixes) title = prefix.Replace(title, "").Trim(' ');
        return Whitespace.Replace(title, " ");
    }

    public static string VersionSignature(string title, string path)
    {
        var normalized = Normalize(title);
        var result = new StringBuilder();
        for (var i = 0; i < VersionMarkers.Length; i++)
            if (VersionMarkers[i].IsMatch(normalized)) result.Append(i).Append(',');
        var audioName = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        var speedMatch = SpeedVersion.Match(title + " " + audioName);
        if (speedMatch.Success && double.TryParse(speedMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var speed)
            && speed != 1)
            result.Append("speed:").Append(speed.ToString("G", CultureInfo.InvariantCulture)).Append(',');
        return result.ToString();
    }

    private static Regex Marker(string pattern) => new(pattern, RegexOptions.ECMAScript | RegexOptions.Compiled);

    private static string JsLower(string value)
    {
        // JavaScript uses Unicode full lowercase mappings. .NET's invariant mapping leaves İ unchanged
        // and does not apply the contextual final-sigma rule, so handle those before simple lowercasing.
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\u0130') result.Append("i\u0307");
            else if (character == '\u03A3' && IsFinalSigma(value, index)) result.Append('\u03C2');
            else result.Append(character switch
            {
                // Node's bundled Unicode data contains these lowercase mappings; the local .NET
                // runtime currently leaves their uppercase forms unchanged.
                '\u1C89' => '\u1C8A',
                '\uA7CB' => '\u0264',
                '\uA7CC' => '\uA7CD',
                '\uA7CE' => '\uA7CF',
                '\uA7D2' => '\uA7D3',
                '\uA7D4' => '\uA7D5',
                '\uA7DA' => '\uA7DB',
                '\uA7DC' => '\u019B',
                _ => character,
            });
        }
        return result.ToString().ToLowerInvariant();
    }

    private static bool IsFinalSigma(string value, int index)
    {
        var before = index - 1;
        while (before >= 0 && IsCaseIgnorable(value[before])) before--;
        if (before < 0 || !IsCased(value[before])) return false;
        var after = index + 1;
        while (after < value.Length && IsCaseIgnorable(value[after])) after++;
        return after >= value.Length || !IsCased(value[after]);
    }

    private static bool IsCased(char value) => CharUnicodeInfo.GetUnicodeCategory(value) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter;

    private static bool IsCaseIgnorable(char value) => value is '\'' or '\u2019'
        || CharUnicodeInfo.GetUnicodeCategory(value) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
            or UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol;
}
