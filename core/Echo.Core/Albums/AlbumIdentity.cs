using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Echo.Core.Albums;

/// <summary>Persisted album key contract, shared by incremental writes and rebuilding.</summary>
internal static class AlbumIdentity
{
    public const string VersionKey = "album_grouping_release_evidence_v8";
    internal const string SpaceCharacters = @"\u0009-\u000d\u0020\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff";
    internal const string SpacePattern = "[" + SpaceCharacters + "]";
    private static readonly Regex Whitespace = new(SpacePattern + "+", RegexOptions.Compiled);
    private static readonly Regex EdgeWhitespace = new("^" + SpacePattern + "+|" + SpacePattern + "+$", RegexOptions.Compiled);
    private static readonly Regex ZeroWidth = new(@"[\u200b-\u200f\u202a-\u202e\u2060\ufeff]", RegexOptions.Compiled);
    private static readonly Regex Combining = new(@"[\u0300-\u036f]", RegexOptions.Compiled);
    private static readonly Regex Disc = new(@"^(?:cd|disc|disk|vol(?:ume)?\.?|part)\s*[-_.#]?\s*(?:[0-9]{1,3}|[ivxlcdm]{1,8})(?:\s*\[flac\])?$".Replace(@"\s", SpacePattern),
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DescribedDisc = new((@"^(?:cd|disc|disk|vol(?:ume)?\.?|part)\s*[-_.#]?\s*([0-9]{1,3}|[ivxlcdm]{1,8})(?:\s+[-–—]\s+[^" + SpaceCharacters + @"].*|\s+\([^()]+\))$").Replace(@"\s", SpacePattern),
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    public static string Display(string? value) => Whitespace.Replace(value ?? "", " ").Trim(' ');
    internal static string Trim(string value) => EdgeWhitespace.Replace(value, "");
    public static string Normalize(string? value) => Display(ZeroWidth.Replace(AlbumUnicodeNormalization.Normalize(value ?? "", NormalizationForm.FormKC), ""));
    public static string KeyPart(string? value) => Lower(Normalize(value));
    public static string ArtistKey(string? value) => Filter(Lower(Fold(Normalize(value))), category =>
        category is not (UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
        or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
        or UnicodeCategory.OtherPunctuation or UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator
        or UnicodeCategory.ParagraphSeparator or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol
        or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol));
    public static string LooseTitle(string? value)
    {
        var folded = Lower(Fold(value ?? ""));
        var text = new StringBuilder();
        foreach (var rune in folded.EnumerateRunes())
        {
            var category = AlbumUnicodeData.Category(rune);
            text.Append(category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber
                or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber ? rune.ToString() : " ");
        }
        var normalized = Display(text.ToString());
        return normalized.Length > 0 ? normalized : KeyPart(value);
    }
    private static string Fold(string value) => AlbumUnicodeNormalization.Normalize(Combining.Replace(
        AlbumUnicodeNormalization.Normalize(value, NormalizationForm.FormKD), ""), NormalizationForm.FormKC);
    private static string Filter(string value, Func<UnicodeCategory, bool> keep)
    {
        var result = new StringBuilder();
        foreach (var rune in value.EnumerateRunes()) if (keep(AlbumUnicodeData.Category(rune))) result.Append(rune);
        return result.ToString();
    }

    // JavaScript lowercasing uses full Unicode mappings (including dotted I and
    // contextual final sigma); invariant .NET simple casing alone changes keys.
    internal static string Lower(string value)
    {
        var runes = value.EnumerateRunes().ToArray();
        var result = new StringBuilder(value.Length);
        bool Cased(int index, int direction)
        {
            for (; index >= 0 && index < runes.Length; index += direction)
            {
                var rune = runes[index];
                var category = AlbumUnicodeData.Category(rune);
                if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
                    or UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol
                    || rune.Value is 0x27 or 0x2e or 0x3a or 0xb7 or 0x387 or 0x55f or 0x5f4
                        or 0x2018 or 0x2019 or 0x2024 or 0x2027 or 0xfe13 or 0xfe52 or 0xfe55 or 0xff07 or 0xff0e or 0xff1a) continue;
                return category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                    || Rune.ToLowerInvariant(rune) != Rune.ToUpperInvariant(rune);
            }
            return false;
        }
        for (var i = 0; i < runes.Length; i++)
        {
            if (runes[i].Value == 0x3a3 && Cased(i - 1, -1) && !Cased(i + 1, 1)) result.Append('\u03c2');
            else result.Append(AlbumUnicodeData.Lower(runes[i]));
        }
        return result.ToString();
    }

    public static bool IsDiscFolder(string name, bool allowDescription = true) =>
        Disc.IsMatch(name) || allowDescription && DescribedDisc.IsMatch(name);
    public static string Folder(string path, bool? windows = null)
    {
        var isWindows = windows ?? OperatingSystem.IsWindows();
        var directory = Directory(path, isWindows);
        var index = LastSeparator(directory, isWindows);
        var name = Trim(directory[(index + 1)..]);
        return IsDiscFolder(name) ? Directory(directory, isWindows) : directory;
    }
    private static int LastSeparator(string path, bool windows) => windows ? Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) : path.LastIndexOf('/');
    private static string Directory(string path, bool windows)
    {
        var root = windows && path.Length >= 3 && path[1] == ':' && path[2] is '/' or '\\' ? 3
            : path.StartsWith('/') || windows && path.StartsWith('\\') ? 1 : 0;
        if (windows && path.Length > 2 && path[0] is '/' or '\\' && path[1] == path[0])
        {
            var separator = path[0];
            var server = path.IndexOf(separator, 2);
            var share = server < 0 ? -1 : path.IndexOf(separator, server + 1);
            root = share < 0 ? path.Length : share + 1;
        }
        var end = path.Length;
        while (end > root && (path[end - 1] == '/' || windows && path[end - 1] == '\\')) end--;
        var index = LastSeparator(path[..end], windows);
        if (index < root) return root > 0 ? path[..root] : ".";
        while (index > root && (path[index - 1] == '/' || windows && path[index - 1] == '\\')) index--;
        return path[..index];
    }
    public static string Hash(string value) => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Key(string title, string path, string trackId, string resolvedArtist = "", bool? windows = null, long? discNo = null, AlbumDiscMetadata.Facts? facts = null)
    {
        var loose = LooseTitle(title);
        var disc = facts ?? AlbumDiscMetadata.Read(title, path, discNo, windows);
        return loose.Length == 0 || loose == "unknown album" ? "unknown:" + trackId
            : Hash("folder:" + KeyPart(Folder(path, windows)) + "\0" + resolvedArtist + "\0" + (disc.NormalizeTitle ? LooseTitle(disc.Title) : KeyPart(disc.Title)));
    }
    internal static string ImmediateDirectoryName(string path, bool? windows = null)
    {
        var isWindows = windows ?? OperatingSystem.IsWindows();
        var directory = Directory(path, isWindows);
        return Trim(directory[(LastSeparator(directory, isWindows) + 1)..]);
    }
    public static bool Reliable(string? source) => source is "embedded" or "manual" or "network" or "sidecar" or "technical";
    public static string Source(string? json, string field)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? "{}");
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(field, out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }
    public static bool Include(string title, string json) => !Lower(Trim(title)).StartsWith("osu! beatmapset ", StringComparison.Ordinal)
        && !(Trim(title).Length == 0 && (Source(json, "album") == "sidecar" || Lower(Trim(Source(json, "osu"))) == "osu"));
}
