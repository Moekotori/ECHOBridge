using System.Text;

namespace Echo.Core.Albums;

/// <summary>Preserves Electron's Unicode 17 keys on hosts with older ICU/NLS normalization.</summary>
internal static class AlbumUnicodeNormalization
{
    public static string Normalize(string value, NormalizationForm form)
    {
        if (form is not (NormalizationForm.FormKC or NormalizationForm.FormKD)) throw new ArgumentException("Unsupported album normalization.");
        var needed = false;
        foreach (var rune in value.EnumerateRunes())
            if (AlbumUnicodeData.NeedsNormalization(rune.Value)) { needed = true; break; }
        if (!needed) return value.Normalize(form);

        var expanded = new StringBuilder(value.Length);
        var chunk = new StringBuilder(value.Length);
        void FlushChunk()
        {
            if (chunk.Length == 0) return;
            expanded.Append(chunk.ToString().Normalize(NormalizationForm.FormKD));
            chunk.Clear();
        }
        foreach (var rune in value.EnumerateRunes())
        {
            // This noncharacter is preserved by JavaScript but rejected by some .NET normalizers.
            if (rune.Value == 0xfffe) { FlushChunk(); expanded.Append(rune); }
            else chunk.Append(AlbumUnicodeData.Decomposition(rune.Value) ?? rune.ToString());
        }
        FlushChunk();
        var ordered = Order(expanded.ToString());
        return form == NormalizationForm.FormKD ? ordered : Compose(ordered);
    }
    private static string Order(string value)
    {
        var output = new StringBuilder(value.Length);
        var pending = new List<(Rune Rune, int Rank, int Order)>();
        void Flush()
        {
            pending.Sort((a, b) => a.Rank == b.Rank ? a.Order.CompareTo(b.Order) : a.Rank.CompareTo(b.Rank));
            foreach (var entry in pending) output.Append(entry.Rune);
            pending.Clear();
        }
        foreach (var rune in value.EnumerateRunes())
        {
            var rank = AlbumCanonicalClasses.For(rune.Value);
            if (rank == 0) { Flush(); output.Append(rune); }
            else pending.Add((rune, rank, pending.Count));
        }
        Flush();
        return output.ToString();
    }
    private static string Compose(string value)
    {
        var output = new List<Rune>(value.Length);
        var starter = -1; var previousRank = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var rank = AlbumCanonicalClasses.For(rune.Value);
            var composed = -1;
            if (starter >= 0 && (previousRank == 0 || previousRank < rank))
            {
                var left = output[starter];
                composed = AlbumUnicodeData.Composition(left.Value, rune.Value);
                if (composed < 0 && left.Value != 0xfffe && rune.Value != 0xfffe && !(left.IsAscii && rune.IsAscii))
                {
                    var text = (left.ToString() + rune).Normalize(NormalizationForm.FormC);
                    var enumerator = text.EnumerateRunes().GetEnumerator();
                    if (enumerator.MoveNext()) { var first = enumerator.Current; if (!enumerator.MoveNext()) composed = first.Value; }
                }
            }
            if (composed >= 0) { output[starter] = new Rune(composed); continue; }
            if (rank == 0) starter = output.Count;
            output.Add(rune);
            previousRank = rank;
        }
        var result = new StringBuilder(value.Length);
        foreach (var rune in output) result.Append(rune);
        return result.ToString();
    }
}
