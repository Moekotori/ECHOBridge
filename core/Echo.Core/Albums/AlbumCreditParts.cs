using System.Text;
using System.Text.RegularExpressions;

namespace Echo.Core.Albums;

/// <summary>Literal credit participants used for visual-cover compatibility, before display heuristics.</summary>
internal static class AlbumCreditParts
{
    private static readonly Regex Separator = new(@"\s*(?:/|,(?!\s)|;|；|×)\s*|\s+(?:feat\.?|ft\.?|featuring|with|x)\s+".Replace(@"\s", AlbumIdentity.SpacePattern),
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CompactSlash = new(@"^[a-z0-9]{1,3}/[a-z0-9]{1,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NumericSlash = new(@"(\p{N})\s*/\s*(?=\p{N})".Replace(@"\s", AlbumIdentity.SpacePattern), RegexOptions.Compiled);
    public static IEnumerable<string> Split(string value)
    {
        var trimmed = AlbumIdentity.Trim(value);
        if (CompactSlash.IsMatch(AlbumUnicodeNormalization.Normalize(trimmed, NormalizationForm.FormKC))) { yield return trimmed; yield break; }
        foreach (var part in Separator.Split(NumericSlash.Replace(trimmed, "$1\u0001")))
        {
            var name = AlbumIdentity.Trim(part.Replace('\u0001', '/'));
            if (name.Length > 0) yield return name;
        }
    }
}
