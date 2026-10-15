using System.Globalization;
using System.Text;

namespace Echo.Core.Query;

public static class GenreKey
{
    public const string Unclassified = "__unclassified__";

    public static string FromTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Unclassified;
        }

        var key = value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        return key.Length > 0 ? key : Unclassified;
    }

    public static string FromSqlValue(object? value) => value switch
    {
        null or DBNull => Unclassified,
        string text => FromTag(text),
        long number => FromTag(number.ToString(CultureInfo.InvariantCulture)),
        int number => FromTag(number.ToString(CultureInfo.InvariantCulture)),
        double number => FromTag(number.ToString(CultureInfo.InvariantCulture)),
        _ => FromTag(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    public static bool IsUnclassified(string? value) =>
        value == Unclassified || FromTag(value) == Unclassified;
}
