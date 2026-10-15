using System.Text.RegularExpressions;

namespace Echo.Core.Albums;

internal static class AlbumDiscMetadata
{
    internal sealed record Facts(string Title, long? DiscNo, bool NormalizeTitle);
    private static readonly Regex FolderNumber = new(@"^(?:cd|disc|disk|vol(?:ume)?\.?|part)\s*[-_.#]?\s*([0-9]{1,3}|[ivxlcdm]{1,8})".Replace(@"\s", AlbumIdentity.SpacePattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Suffix = new(@"(?:\s*\((?:cd|disc|disk)\s*[-_.#]?\s*([0-9]{1,3}|[ivxlcdm]{1,8})\)|\s*\[(?:cd|disc|disk)\s*[-_.#]?\s*([0-9]{1,3}|[ivxlcdm]{1,8})\]|\s+(?:cd|disc|disk)\s*[-_.#]?\s*([0-9]{1,3}|[ivxlcdm]{1,8}))$".Replace(@"\s", AlbumIdentity.SpacePattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static long? Number(string text)
    {
        if (long.TryParse(text, out var number)) return number is > 0 and <= 999 ? number : null;
        int Roman(char c) => char.ToLowerInvariant(c) switch { 'i' => 1, 'v' => 5, 'x' => 10, 'l' => 50, 'c' => 100, 'd' => 500, 'm' => 1000, _ => 0 };
        number = 0;
        for (var i = 0; i < text.Length; i++) number += i + 1 < text.Length && Roman(text[i]) < Roman(text[i + 1]) ? -Roman(text[i]) : Roman(text[i]);
        return number is > 0 and <= 999 ? number : null;
    }
    public static Facts Read(string title, string path, long? discNo = null, bool? windows = null)
    {
        var name = AlbumIdentity.ImmediateDirectoryName(path, windows);
        var folder = AlbumIdentity.IsDiscFolder(name);
        var directoryDisc = folder ? Number(FolderNumber.Match(name).Groups[1].Value) : null;
        var explicitDisc = discNo > 0 ? discNo : null;
        var trimmed = AlbumIdentity.Trim(title);
        var suffix = Suffix.Match(trimmed);
        var titleDisc = suffix.Success ? Number(suffix.Groups.Cast<Group>().Skip(1).First(group => group.Success).Value) : null;
        var expected = explicitDisc ?? directoryDisc;
        var baseTitle = suffix.Success ? AlbumIdentity.Trim(trimmed[..suffix.Index]) : "";
        var compatible = (explicitDisc is null || directoryDisc is null || explicitDisc == directoryDisc)
            && (expected is null || titleDisc is null || expected == titleDisc);
        var strip = compatible && titleDisc is not null && baseTitle.Length > 0 && AlbumIdentity.KeyPart(baseTitle) != "unknown album";
        return new(strip ? baseTitle : title, explicitDisc ?? directoryDisc ?? (strip ? titleDisc : null), compatible && (folder || strip));
    }
}
