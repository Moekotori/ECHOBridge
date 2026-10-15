using Echo.Core.Database;
using Echo.Core.Protocol;

namespace Echo.Core.Writes;

/// <summary>Cover-only maintenance of existing standard albums. Never changes album identity or links.</summary>
internal static class ScanAlbumCoverRefreshWriter
{
    public const string Capability = "library.scanAlbumCovers/v1";

    public static LibraryMutationResult Refresh(TransactionSql sql, ScanAlbumCoverRefresh request)
    {
        if (request.TrackIds is null || request.TrackIds.Count > 128 || string.IsNullOrEmpty(request.UpdatedAt) || request.UpdatedAt.Length > 128
            || request.TrackIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 512))
            throw new ArgumentException("Invalid album cover refresh batch.");
        var ids = request.TrackIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return Result(false, []);
        var parameters = ids.Select((_, index) => "$" + index).ToArray();
        var values = ids.Cast<object?>().ToArray();
        var predicate = "tracks.id IN (" + string.Join(',', parameters) + ") AND tracks.missing=0";
        // Decide eligibility before any album write. Unlinked tracks still need the existing grouping algorithm.
        var linked = Convert.ToInt32(sql.Value("""
            SELECT COUNT(DISTINCT tracks.id) FROM tracks
            INNER JOIN album_tracks ON album_tracks.track_id=tracks.id
            INNER JOIN albums ON albums.id=album_tracks.album_id WHERE
            """ + " " + predicate, values));
        if (linked != ids.Length) return Result(true, []);

        var albums = new List<string>(128);
        using (var command = sql.Command("""
            SELECT DISTINCT albums.id FROM tracks
            INNER JOIN album_tracks ON album_tracks.track_id=tracks.id
            INNER JOIN albums ON albums.id=album_tracks.album_id WHERE
            """ + " " + predicate + " ORDER BY albums.id LIMIT 129", values))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) albums.Add(reader.GetString(0));
        if (albums.Count > 128) return Result(true, []);
        if (albums.Any(id => id.Length > 512)) return Result(true, []);

        foreach (var albumId in albums)
        {
            // Match the existing seed path: first nonempty cover in link order, including hidden tracks,
            // excluding missing tracks. No tags, PCM, cover bytes or whole album rows leave Core.
            sql.Run("""
                UPDATE albums SET cover_id=(
                  SELECT tracks.cover_id FROM album_tracks
                  INNER JOIN tracks ON tracks.id=album_tracks.track_id
                  WHERE album_tracks.album_id=$0 AND tracks.missing=0
                    AND tracks.cover_id IS NOT NULL AND tracks.cover_id!=''
                  ORDER BY album_tracks.position ASC LIMIT 1
                ),updated_at=$1 WHERE id=$0
                """, albumId, request.UpdatedAt);
        }
        return Result(false, albums);
    }

    private static LibraryMutationResult Result(bool requiresGrouping, List<string> albums) =>
        new(AlbumCovers: new(requiresGrouping, albums));
}
