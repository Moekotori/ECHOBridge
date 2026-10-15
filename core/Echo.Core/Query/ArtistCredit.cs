using System.Text;
using System.Text.RegularExpressions;

namespace Echo.Core.Query;

public static class ArtistCredit
{
    public const string VariousArtistsKey = "various artists";
    public const string VariousArtistsDisplayName = "Various Artists";

    private static readonly Regex ZeroWidth = new(@"[\u200b-\u200f\u202a-\u202e\u2060\ufeff]", RegexOptions.Compiled);
    private static readonly Regex CombiningMark = new(@"[\u0300-\u036f]", RegexOptions.Compiled);
    private static readonly Regex PunctuationAndSpace = new(@"[\p{P}\p{Z}\p{S}]", RegexOptions.Compiled);
    private static readonly Regex ArtistNameSeparator = new(
        @"\s*(?:/|,|;|；|×)\s*|(?<!\s)&(?!\s)|\s+(?:feat\.?|ft\.?|featuring|with|x)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CompactSlashBandName = new(@"^[A-Z0-9]{1,3}/[A-Z0-9]{1,3}$", RegexOptions.Compiled);
    private static readonly Regex NumericSlash = new(@"(\p{N})\s*/\s*(?=\p{N})", RegexOptions.Compiled);
    private static readonly Regex[] StandardSuffixes =
    [
        new(@"\s*(?:\+|\uff0b)\s*81$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(@"\s*(?:-|–|—|:|\|)\s*(?:official|topic|vevo)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
    ];

    public static string NormalizeStrategy(string? strategy) =>
        strategy is "conservative" or "standard" ? strategy : "standard";

    public static string MergeKeyForName(string? name, string? strategy)
    {
        var normalized = NormalizeDisplay(name);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return CompactKey(NormalizeStrategy(strategy) == "conservative" ? normalized : StripStandardSuffixes(normalized));
    }

    public static bool Matches(string? value, string? key, string? strategy)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        foreach (var part in SplitNames(value))
        {
            if (string.Equals(MergeKeyForName(part, strategy), key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsVariousArtistKey(string? artistKey, string? strategy) =>
        artistKey == VariousArtistsKey
        || string.Equals(artistKey, MergeKeyForName(VariousArtistsDisplayName, strategy), StringComparison.Ordinal);

    private static IEnumerable<string> SplitNames(string? value)
    {
        var normalized = NormalizeWhitespace(value);
        if (normalized.Length == 0)
        {
            yield break;
        }

        if (CompactSlashBandName.IsMatch(normalized))
        {
            yield return normalized;
            yield break;
        }

        var protectedValue = NumericSlash.Replace(normalized, "$1\u0001");
        foreach (var part in ArtistNameSeparator.Split(protectedValue))
        {
            var name = NormalizeWhitespace(part.Replace('\u0001', '/'));
            if (name.Length > 0)
            {
                yield return name;
            }
        }
    }

    private static string NormalizeDisplay(string? value)
    {
        var text = NormalizeWhitespace(value);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return ZeroWidth.Replace(text.Normalize(NormalizationForm.FormKC), string.Empty);
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private static string StripStandardSuffixes(string value)
    {
        var current = NormalizeDisplay(value);
        foreach (var pattern in StandardSuffixes)
        {
            current = pattern.Replace(current, string.Empty).Trim();
        }

        return current.Length > 0 ? current : NormalizeDisplay(value);
    }

    private static string CompactKey(string value)
    {
        var folded = CombiningMark.Replace(value.Normalize(NormalizationForm.FormKD), string.Empty)
            .Normalize(NormalizationForm.FormKC)
            .ToLowerInvariant();
        return PunctuationAndSpace.Replace(folded, string.Empty);
    }
}
