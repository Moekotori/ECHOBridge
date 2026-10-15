using System.Text;
using System.Text.RegularExpressions;
using Echo.Core.Protocol;

namespace Echo.Core.Search;

/// <summary>UI-independent search preparation shared by the current host and future native UI.</summary>
public static class LibrarySearchNormalizer
{
    public const string Capability = "search.raw/v1";
    private const string Separators = "[\\s!\"#$%&'()*+,./:;<=>?@[\\\\\\]^`{|}~_-]+";
    private static bool IsCjk(string text) => text.EnumerateRunes().Any(r => SearchUnicodeRanges.IsCjk(r.Value));
    private static string Check(string? text)
    {
        text ??= "";
        if (text.Length > 4096) throw new ArgumentException("Search exceeds the 4096-character limit.");
        return text;
    }
    private static string Term(string value, bool exact = false) => !exact
        && value.Length > 0 && value.EnumerateRunes().All(r => r.Value == '_' || Rune.IsLetter(r)
            || Rune.GetUnicodeCategory(r) is System.Globalization.UnicodeCategory.DecimalDigitNumber
                or System.Globalization.UnicodeCategory.LetterNumber or System.Globalization.UnicodeCategory.OtherNumber)
        && value.ToLowerInvariant() is not ("and" or "or" or "not")
        ? value + "*" : "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string Group(IEnumerable<string> clauses)
    {
        var items = clauses.ToArray();
        return items.Length == 1 ? items[0] : "(" + string.Join(" OR ", items) + ")";
    }
    public static CoreNormalizedTrackSearch Track(string? text, bool crossScript = true)
    {
        var parsed = LibrarySearchParser.Parse(Check(text));
        var clauses = new List<string>();
        foreach (var term in parsed.Terms.Distinct(StringComparer.Ordinal).Take(12))
        {
            var pieces = Regex.Split(term, Separators).Where(x => x.Length > 0).ToArray();
            foreach (var piece in pieces.Length == 0 ? [term] : pieces)
                clauses.Add(Group(ChineseSearchVariants.Expand(piece, crossScript)
                    .SelectMany(JapaneseSearchVariants.Expand).Distinct(StringComparer.Ordinal).Select(value => Term(value))));
        }
        foreach (var exact in parsed.Exact.Distinct(StringComparer.Ordinal).Take(8))
            clauses.Add(Group(ChineseSearchVariants.Expand(exact, crossScript).Select(value => Term(value, true))));
        foreach (var (field, values) in parsed.Fields)
            foreach (var value in values)
                clauses.Add(Group((field == "artist" ? new[] { "artist", "album_artist" } : [field])
                    .SelectMany(column => ChineseSearchVariants.Expand(value, crossScript).Select(variant => column + ":" + Term(variant)))));
        return new()
        {
            FtsMatch = clauses.Count > 0 ? string.Join(" AND ", clauses) : null,
            Year = parsed.Year,
            Excludes = parsed.Exclude.Distinct(StringComparer.Ordinal).Take(8).Select(value => new CoreSearchExclude
                { Substring = IsCjk(value), Variants = ChineseSearchVariants.Expand(value, crossScript) }).ToList(),
        };
    }
    public static CoreNormalizedEntitySearch Entity(string? text, bool crossScript = true)
    {
        var normalized = Check(text).Normalize(NormalizationForm.FormKC).Trim();
        var parts = Regex.Split(normalized, Separators).Where(x => x.Length > 0).ToArray();
        IEnumerable<string> terms = parts.Length == 1 && IsCjk(parts[0]) && parts[0].EnumerateRunes().Count() > 2
            ? parts[0].EnumerateRunes().Select(r => r.ToString()) : parts;
        return new() { Terms = terms.Distinct(StringComparer.Ordinal).Take(12)
            .Select(term => new CoreNormalizedEntitySearchTerm { Variants = ChineseSearchVariants.Expand(term, crossScript)
                .SelectMany(JapaneseSearchVariants.Expand).Distinct(StringComparer.Ordinal).ToList() }).ToList() };
    }
    internal static void Prepare(CoreRequest request)
    {
        if (request.Params is not { NormalizeSearch: true } parameters) return;
        var crossScript = parameters.SearchOptions?.ChineseCrossScriptSearchEnabled != false;
        if (request.Method == "getTracks") parameters.NormalizedSearch = Track(parameters.Query?.Search, crossScript);
        else parameters.NormalizedEntitySearch = Entity(parameters.HistoryQuery?.Search ?? parameters.Query?.Search, crossScript);
    }
}
