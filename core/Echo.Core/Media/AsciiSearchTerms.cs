using System.Text;
using Echo.Core.Protocol;

namespace Echo.Core.Media;

/// <summary>Exact ASCII subset of SearchIndexTokens; CJK and other Unicode stay on the TS worker.</summary>
public static class AsciiSearchTerms
{
    public const string Capability = "media.searchTermsAscii/v1";
    private const int MaxBatch = 64;
    private const int MaxBatchChars = 65_536;
    private const string Separators = "!\"#$%&'()*+,./:;<=>?@[\\]^`{|}~_-";

    public static AsciiSearchTermsResult Prepare(AsciiSearchTermsRequest request)
    {
        if (request.Items.Count > MaxBatch) throw new ArgumentException("ASCII search batch is too large.");
        var totalChars = 0;
        var output = new List<string>(request.Items.Count);
        foreach (var fields in request.Items)
        {
            var values = new[] { fields.Title, fields.Artist, fields.Album, fields.AlbumArtist,
                fields.Genre, fields.Path, fields.RemotePath };
            foreach (var value in values)
            {
                if (value is null) continue;
                totalChars += value.Length;
                if (totalChars > MaxBatchChars || value.Any(ch => ch > 0x7f || ch == '\0'))
                    throw new ArgumentException("Search batch requires legacy Unicode processing.");
            }
            output.Add(PrepareOne(values));
        }
        return new AsciiSearchTermsResult(output);
    }

    private static string PrepareOne(string?[] values)
    {
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string value) { if (value.Length > 0 && seen.Add(value)) terms.Add(value); }
        foreach (var value in values)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized)) continue;
            Add(normalized);
            var parts = new List<string>();
            var current = new StringBuilder();
            void Flush()
            {
                if (current.Length == 0) return;
                var part = current.ToString();
                parts.Add(part);
                Add(part);
                current.Clear();
            }
            foreach (var ch in normalized)
            {
                if (char.IsWhiteSpace(ch) || Separators.Contains(ch)) Flush();
                else current.Append(ch);
            }
            Flush();
            if (parts.Count > 1) Add(string.Concat(parts));
        }
        // The async TS entrypoint splits the synchronous term string on literal spaces
        // before adding romanization. ASCII has no romanization, but this dedup pass matters.
        var tokens = new List<string>();
        var tokenSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in string.Join(' ', terms).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (tokenSet.Add(token)) tokens.Add(token);
        return string.Join(' ', tokens);
    }
}
