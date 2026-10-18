using System.Text;
using System.Text.RegularExpressions;

namespace Echo.Core.Playlists;

/// <summary>Literal title and complete artist credits. No aliases, subset matching or version stripping.</summary>
internal static class LastFmImportIdentity
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex CompactSlash = new(@"\b[A-Z0-9]{1,3}/[A-Z0-9]{1,3}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CreditSeparator = new(@"\s*[/,;；×]\s*|\s+(?:feat\.?|ft\.?|featuring)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string NormalizeText(string value) => Whitespace.Replace(value.Normalize(NormalizationForm.FormKC), " ").Trim();

    public static string Key(string artist, string title)
    {
        var credit = NormalizeText(artist);
        // Preserve compact band names and fractions before splitting collaboration separators.
        var protectedCredit = CompactSlash.Replace(credit, match => match.Value.Replace('/', '\0'));
        var participants = CreditSeparator.Split(protectedCredit)
            .Select(part => part.Replace('\0', '/').Trim())
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (participants.Length > 1 ? string.Join('\0', participants) : credit) + "\0\0" + NormalizeText(title);
    }
}
