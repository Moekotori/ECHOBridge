using System.Text;
using System.Text.RegularExpressions;

namespace Echo.Core.Search;

/// <summary>Bounded spelling aliases matching the Electron search adapter; no dictionary or query cache.</summary>
internal static class JapaneseSearchVariants
{
    private const int MaxInputLength = 512;
    private const int MaxVariants = 6;
    private static readonly Regex Syllable = new("^(?:(?:ky|gy|ny|hy|by|py|my|ry|sy|ty|zy)[auo]|shi|chi|tsu|[kstnhmyrwgzdbpfvj]?[aeiou]|n'?)", RegexOptions.CultureInvariant);
    private static readonly Regex KeyboardSpelling = new("sya|syu|syo|tya|tyu|tyo|zya|zyu|zyo|si|ti|tu|hu|zi", RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, string> Spellings = new(StringComparer.Ordinal)
    {
        ["sya"] = "sha", ["syu"] = "shu", ["syo"] = "sho",
        ["tya"] = "cha", ["tyu"] = "chu", ["tyo"] = "cho",
        ["zya"] = "ja", ["zyu"] = "ju", ["zyo"] = "jo",
        ["si"] = "shi", ["ti"] = "chi", ["tu"] = "tsu", ["hu"] = "fu", ["zi"] = "ji",
    };

    private static bool IsReading(string value)
    {
        if (value.Length == 0) return false;
        for (var index = 0; index < value.Length;)
        {
            var current = value[index];
            if (("bcdfghjklmnpqrstvwxyz".Contains(current) && index + 1 < value.Length && current == value[index + 1])
                || (current == 't' && index + 2 < value.Length && value[index + 1] == 'c' && value[index + 2] == 'h'))
            {
                index++;
                continue;
            }
            // Only a three-character window is matched, avoiding regex backtracking on long input.
            var match = Syllable.Match(value.Substring(index, Math.Min(3, value.Length - index)));
            if (!match.Success) return false;
            index += match.Length;
        }
        return true;
    }

    private static bool IsKana(int value) => value is >= 0x3041 and <= 0x309f or >= 0x30a1 and <= 0x30ff
        or >= 0x31f0 and <= 0x31ff or >= 0xff66 and <= 0xff9d;

    // Includes supplementary ideographs; a kana/Latin query must not initialize OpenCC dictionaries.
    internal static bool HasHan(string value) => value.EnumerateRunes().Any(r => r.Value is
        >= 0x2e80 and <= 0x2eff or >= 0x2f00 and <= 0x2fdf or 0x3005 or 0x3007
        or >= 0x3021 and <= 0x3029 or >= 0x3038 and <= 0x303b
        or >= 0x3400 and <= 0x4dbf or >= 0x4e00 and <= 0x9fff
        or >= 0xf900 and <= 0xfaff or >= 0x20000 and <= 0x33479);

    public static List<string> Expand(string term)
    {
        var variants = new List<string> { term };
        if (term.Length > MaxInputLength) return variants;
        void Add(string value)
        {
            if (variants.Count < MaxVariants && !variants.Contains(value, StringComparer.Ordinal)) variants.Add(value);
        }
        if (term.EnumerateRunes().Any(r => IsKana(r.Value)))
        {
            var hiragana = string.Concat(term.Select(c => c is >= '\u30a1' and <= '\u30f6' ? (char)(c - 0x60) : c));
            Add(hiragana);
            Add(string.Concat(hiragana.Select(c => c is >= '\u3041' and <= '\u3096' ? (char)(c + 0x60) : c)));
        }
        else
        {
            var lower = term.ToLowerInvariant();
            var normalized = string.Concat(lower.Normalize(NormalizationForm.FormKD)
                .Where(c => c is not (>= '\u0300' and <= '\u036f') && c is not ('\'' or '’')));
            if (!IsReading(normalized)) return variants;
            if (normalized != lower) Add(normalized);
            var canonical = KeyboardSpelling.Replace(normalized, match => Spellings[match.Value]);
            if (canonical != normalized) Add(canonical);
        }
        foreach (var value in variants.ToArray())
            Add(value.Replace("ou", "o", StringComparison.Ordinal).Replace("uu", "u", StringComparison.Ordinal));
        return variants;
    }
}
