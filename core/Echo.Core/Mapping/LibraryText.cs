using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Echo.Core.Mapping;

public static class LibraryText
{
    public const string UnknownArtist = "Unknown Artist";
    public const int DisplayLimit = 512;
    public const int TechnicalLimit = 128;
    private static readonly Regex BinaryPattern = new("(?:APIC|image/(?:jpeg|jpg|png|webp|gif)|JFIF|Exif|\u0000)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ControlWhitespace = new("[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]", RegexOptions.Compiled);
    private static readonly Regex ArtistPrefix = new(@"^\d{1,3}\s*[.)\uff0e\u3002]\s*", RegexOptions.Compiled);
    private static readonly Regex ArtistDashPrefix = new(@"^\d{1,3}\s*[-_]\s*(?=[^\d\s(\u005b\uff08])", RegexOptions.Compiled);

    public static bool IsPlainDisplayText(string? text, int maxLength = DisplayLimit)
    {
        if (text is null || text.Length == 0 || text.Length > maxLength)
        {
            return false;
        }

        for (var index = 0; index < text.Length; index += 1)
        {
            var code = (int)text[index];
            if (code <= 0x1f || (code >= 0x7f && code <= 0x9f))
            {
                return false;
            }

            if (text[index] == ' ' && (index == 0 || index + 1 == text.Length || text[index + 1] == ' '))
            {
                return false;
            }
        }

        return true;
    }

    public static string Sanitize(object? value, string fallback, int maxLength = DisplayLimit)
    {
        if (value is string text && IsPlainDisplayText(text, maxLength))
        {
            return text;
        }

        var raw = Normalize(value);
        if (IsUnsafe(raw, maxLength))
        {
            return fallback;
        }

        var normalized = ControlWhitespace.Replace(raw, " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return IsUnsafe(normalized, maxLength) ? fallback : normalized;
    }

    public static string? SanitizeNullable(object? value, int maxLength = DisplayLimit)
    {
        if (value is null || value is DBNull || value is "")
        {
            return null;
        }

        var sanitized = Sanitize(value, string.Empty, maxLength);
        return string.IsNullOrEmpty(sanitized) ? null : sanitized;
    }

    public static string? TextOrNull(object? value) =>
        value is null or DBNull ? null : Convert.ToString(value);

    public static string? FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var name = Path.GetFileName(path.Trim());
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public static int? IntOrNull(object? value) =>
        value is null or DBNull ? null : Convert.ToInt32(value);

    public static double? DoubleOrNull(object? value) =>
        value is null or DBNull ? null : Convert.ToDouble(value);

    public static (string Title, string? Artist) FilenameFallback(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Trim();
        var parts = name.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (StripArtistSortPrefix(string.Join(" - ", parts.Skip(1))), FilenameArtistFallback(parts[0]));
        }

        var title = StripArtistSortPrefix(name);
        return (string.IsNullOrEmpty(title) ? "Untitled" : title, null);
    }

    public static string CoverUrl(string? coverId, string variant) =>
        string.IsNullOrWhiteSpace(coverId)
            ? string.Empty
            : $"echo-cover://{variant}/{UrlEncoder.Default.Encode(coverId)}";

    public static string? ArtistImageUrl(string artistKey, string variant) =>
        string.IsNullOrWhiteSpace(artistKey) ? null : $"echo-artist-image://{variant}/{UrlEncoder.Default.Encode(artistKey)}";

    public static Dictionary<string, string> ParseFieldSources(object? json)
    {
        var text = TextOrNull(json);
        if (string.IsNullOrWhiteSpace(text) || text is "{}" or "[]")
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    result[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Normalize(object? value) =>
        value is null or DBNull ? string.Empty : Convert.ToString(value)?.Trim() ?? string.Empty;

    private static bool IsUnsafe(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length > maxLength || BinaryPattern.IsMatch(text))
        {
            return true;
        }

        var controlCount = text.Count(static character =>
        {
            var code = (int)character;
            return (code >= 0x00 && code <= 0x1f) || (code >= 0x7f && code <= 0x9f);
        });
        return controlCount >= 8 || controlCount / (double)Math.Max(1, text.Length) > 0.02;
    }

    private static string StripArtistSortPrefix(string value)
    {
        var trimmed = value.Trim();
        var stripped = ArtistDashPrefix.Replace(ArtistPrefix.Replace(trimmed, string.Empty), string.Empty).Trim();
        return string.IsNullOrEmpty(stripped) ? trimmed : stripped;
    }

    private static string? FilenameArtistFallback(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        if (Regex.IsMatch(normalized, @"^\d{1,2}$") || Regex.IsMatch(normalized, @"^0+\d*$"))
        {
            return null;
        }

        return Sanitize(value, UnknownArtist);
    }
}
