using System.Text;
using System.Text.RegularExpressions;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class ArtistFactsQuery
{
    internal static IEnumerable<string> CreditParts(string value)
    {
        var trimmed = value.Trim();
        if (Regex.IsMatch(trimmed.Normalize(NormalizationForm.FormKC), @"^[a-z0-9]{1,3}/[a-z0-9]{1,3}$", RegexOptions.IgnoreCase)) return [trimmed];
        return Regex.Split(Regex.Replace(trimmed, @"(\p{N})\s*/\s*(?=\p{N})", "$1\u0001"),
            @"\s*(?:/|,(?!\s)|;|；|×)\s*|\s+(?:feat\.?|ft\.?|featuring|with|x)\s+", RegexOptions.IgnoreCase)
            .Select(part => part.Replace('\u0001', '/').Trim()).Where(part => part.Length > 0);
    }
    internal static string PersonKey(string name) => Regex.Replace(name.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ").ToLowerInvariant();
    private static bool Primary(string credit, string name) => CreditParts(credit)
        .SelectMany(part => Regex.Split(part, @"\s+&\s+")).Any(part => part.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant() == name.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant());

    public static ArtistFactsSourceDto Execute(ReadOnlyLibraryDatabase database, string id, CoreSearchOptions? options)
    {
        using var context = new ArtistReadContext(database, options);
        using var transaction = database.BeginDeferredRead();
        var key = AlbumArtistQueries.LocalKey(id);
        ArtistFactsSourceDto Empty() => new(id, null, key is not null, [], 0, 0);
        if (id.StartsWith("album-artist:", StringComparison.Ordinal) && key is null) return Empty();
        var name = Convert.ToString(CachedQuery.Scalar(database, key is null ? "SELECT name FROM artists WHERE id=?" : $"""
            SELECT {AlbumArtistSql.Credit} AS name FROM tracks
            WHERE missing=0 AND library_hidden=0 AND echo_artist_merge_key({AlbumArtistSql.Credit})=?
            GROUP BY name ORDER BY COUNT(*) DESC,name LIMIT 1
            """, c => c.Add(key ?? id)))?.Trim();
        if (string.IsNullOrEmpty(name)) return Empty();
        var linkedSql = key is null ? "SELECT album_id FROM artist_albums WHERE artist_id=?" : $"""
            SELECT album_tracks.album_id FROM tracks JOIN album_tracks ON album_tracks.track_id=tracks.id
            WHERE missing=0 AND library_hidden=0 AND echo_artist_merge_key({AlbumArtistSql.Credit})=?
            """;
        var countSql = key is null ? "SELECT COUNT(*) FROM artist_albums WHERE artist_id=?" : $"SELECT COUNT(DISTINCT album_id) FROM ({linkedSql})";
        if (Convert.ToInt64(CachedQuery.Scalar(database, countSql, c => c.Add(key ?? id))) > 400) return Empty();
        var albums = new List<ArtistLinkedAlbumDto>();
        using (var reader = CachedQuery.Reader(database, $"""
            SELECT albums.id,albums.title,albums.year,albums.cover_id,albums.album_artist,
              SUM(CASE WHEN tracks.id IS NOT NULL AND tracks.play_count>0 THEN 1 ELSE 0 END),
              SUM(CASE WHEN tracks.id IS NOT NULL THEN 1 ELSE 0 END)
            FROM albums LEFT JOIN album_tracks ON album_tracks.album_id=albums.id
            LEFT JOIN tracks ON tracks.id=album_tracks.track_id AND tracks.missing=0 AND tracks.library_hidden=0
            WHERE albums.id IN ({linkedSql}) GROUP BY albums.id,albums.title,albums.year,albums.cover_id,albums.album_artist
            """, c => c.Add(key ?? id)))
        {
            while (reader.Read())
            {
                var credit = reader.GetString(4).Trim();
                if (key is null && !Primary(credit, name)) continue;
                albums.Add(new(reader.GetString(0),reader.GetString(1).Trim(),reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),credit,reader.GetInt32(5),reader.GetInt32(6)));
            }
        }
        var primaryIds = System.Text.Json.JsonSerializer.Serialize(albums.Select(album => album.Id));
        var appearances = Convert.ToInt32(CachedQuery.Scalar(database, """
            SELECT COUNT(DISTINCT artist_tracks.track_id) FROM artist_tracks JOIN tracks ON tracks.id=artist_tracks.track_id
            WHERE artist_tracks.artist_id=? AND missing=0 AND library_hidden=0
              AND NOT EXISTS (SELECT 1 FROM album_tracks WHERE album_tracks.track_id=tracks.id
                AND album_tracks.album_id IN (SELECT value FROM json_each(?)))
            """, c => { c.Add(id);c.Add(primaryIds); }));
        var composers = Convert.ToInt32(CachedQuery.Scalar(database, """
            SELECT COUNT(DISTINCT album_tracks.album_id) FROM music_credits
            JOIN music_credit_roles ON music_credit_roles.id=music_credits.role_id
            JOIN music_people ON music_people.id=music_credits.person_id
            JOIN tracks ON tracks.id=music_credits.track_id JOIN album_tracks ON album_tracks.track_id=tracks.id
            WHERE music_credit_roles.role_key='composer' AND music_people.person_key=? AND tracks.missing=0 AND tracks.library_hidden=0
              AND album_tracks.album_id NOT IN (SELECT value FROM json_each(?))
            """, c => { c.Add(PersonKey(name));c.Add(primaryIds); }));
        return new(id,name,key is not null,albums,appearances,composers);
    }
}
