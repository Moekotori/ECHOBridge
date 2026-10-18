using System.Text;
using Echo.Core.Protocol;

namespace Echo.Bridge.Library;

/// <summary>Check the saved lookup identity without re-running or changing the desktop's accepted match.</summary>
internal static class BridgeLyricCacheIdentity
{
    internal static bool Matches(LibraryTrackDto track, string key, string provider, string origin, string title, string artist)
    {
        if (provider == "manual" || origin == "manual") return true;
        // LyricsService stores provider|query title|query artist|query album|duration[|version flags].
        // Query identity also preserves accepted covers whose provider metadata differs from the local tags.
        var identity = key.Split('|');
        if (identity.Length is 5 or 6)
            return identity[1] == Normalize(track.Title) && identity[2] == Normalize(track.Artist)
                && identity[3] == Normalize(track.Album);
        return Normalize(title) == Normalize(track.Title) && Normalize(artist) == Normalize(track.Artist);
    }

    private static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        var separated = false;
        foreach (var rune in text.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                if (separated && result.Length > 0) result.Append(' ');
                result.Append(Rune.ToLowerInvariant(rune)); separated = false;
            }
            else separated = true;
        }
        return result.ToString();
    }
}
