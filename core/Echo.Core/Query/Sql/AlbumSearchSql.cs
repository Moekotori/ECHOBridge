using Echo.Core.Protocol;

namespace Echo.Core.Query.Sql;

internal static class AlbumSearchSql
{
    private static readonly string[] Metadata = ["albums.title", "albums.album_artist", "COALESCE(CAST(albums.year AS TEXT), '')"];
    private const string TrackText = "tracks.search_terms || ' ' || tracks.title || ' ' || tracks.artist || ' ' || tracks.album_artist || ' ' || COALESCE(tracks.genre, '') || ' ' || tracks.path";

    public static (string Sql, List<object> Parameters) Filter(CoreNormalizedEntitySearch? search)
    {
        if (search?.Terms is not { Count: > 0 } terms) return (string.Empty, []);
        // A phrase can cross the GROUP_CONCAT boundary (including two tracks).
        // Keep that exact path; a term without spaces cannot cross its separators.
        if (terms.Any(term => term.Variants.Any(variant => variant.Contains(' '))))
            return SqlFragments.LikeEntityFilter([.. Metadata, SqlFragments.AlbumSearchBlobSql], search);

        var clauses = new List<string>();
        var parameters = new List<object>();
        foreach (var term in terms)
        {
            var variants = term.Variants.Where(variant => !string.IsNullOrWhiteSpace(variant)).ToArray();
            if (variants.Length == 0) continue;
            var metadata = new List<string>();
            foreach (var variant in variants)
                foreach (var field in Metadata)
                {
                    metadata.Add($"{field} LIKE ? ESCAPE '\\'");
                    parameters.Add($"%{SqlFragments.EscapeLike(variant)}%");
                }
            var tracks = new List<string>();
            foreach (var variant in variants)
            {
                tracks.Add($"({TrackText}) LIKE ? ESCAPE '\\'");
                parameters.Add($"%{SqlFragments.EscapeLike(variant)}%");
            }
            // Scan track text once per term, rather than fetching and rebuilding
            // every album's text. The ID set stays in SQLite, never in the host.
            clauses.Add($"""
                ({string.Join(" OR ", metadata)} OR albums.id IN (
                  SELECT album_tracks.album_id FROM tracks
                  CROSS JOIN album_tracks ON album_tracks.track_id = tracks.id
                  WHERE tracks.missing = 0 AND tracks.library_hidden = 0 AND ({string.Join(" OR ", tracks)})
                ))
                """);
        }
        return (string.Join(" AND ", clauses), parameters);
    }
}
