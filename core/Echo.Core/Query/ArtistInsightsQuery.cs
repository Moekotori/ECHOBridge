using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class ArtistInsightsQuery
{
    internal static LibraryArtistDto? Artist(ReadOnlyLibraryDatabase database, string id)
    {
        const string sql = """
            SELECT artists.*,covers.source_type AS cover_source,
              artist_image_cache.status AS avatar_status,artist_image_cache.provider AS avatar_provider,
              artist_image_cache.source_hash AS avatar_source_hash,artist_image_cache.thumb_path AS avatar_thumb_path,
              artist_image_cache.medium_path AS avatar_medium_path,artist_image_cache.large_path AS avatar_large_path
            FROM artists LEFT JOIN covers ON covers.id=artists.cover_id
            LEFT JOIN artist_image_cache ON artist_image_cache.artist_key=artists.artist_key WHERE artists.id=?
            """;
        using var reader = CachedQuery.Reader(database, sql, c => c.Add(id));
        return reader.Read() ? ArtistRowMapper.Bind(reader).Map() : null;
    }
    public static ArtistInsightsSourceDto Execute(ReadOnlyLibraryDatabase database, string id, CoreArtistInsightsOptions? options, CoreSearchOptions? searchOptions, CancellationToken cancellation)
    {
        using var context = new ArtistReadContext(database, searchOptions);
        using var transaction = database.BeginDeferredRead();
        var grouped = AlbumArtistQueries.LocalKey(id);
        var artist = grouped is null ? Artist(database,id) : AlbumArtistQueries.Artist(database,grouped);
        if (artist is null || grouped is not null) return new(artist,[],[],null);
        var limit = Math.Clamp(options?.Limit ?? 12,4,32);
        var relations = new List<ArtistRelationDto>();
        using (var reader = CachedQuery.Reader(database, """
            WITH relation_rows AS (
              SELECT other.artist_id AS target_id,'same_album' AS kind,COUNT(DISTINCT current.album_id) AS score
              FROM artist_albums current JOIN artist_albums other ON other.album_id=current.album_id
              WHERE current.artist_id=? AND other.artist_id!=current.artist_id GROUP BY other.artist_id
              UNION ALL
              SELECT other.artist_id,'collaboration',COUNT(DISTINCT current.track_id)
              FROM artist_tracks current JOIN artist_tracks other ON other.track_id=current.track_id
              JOIN tracks collaboration_tracks ON collaboration_tracks.id=current.track_id
              WHERE current.artist_id=? AND other.artist_id!=current.artist_id
                AND collaboration_tracks.missing=0 AND collaboration_tracks.library_hidden=0
                AND lower(trim(COALESCE(json_extract(collaboration_tracks.field_sources_json,'$.artist'),''))) IN ('manual','embedded','sidecar','osu','network','technical')
                AND lower(trim(current.source_name))=lower(trim(collaboration_tracks.artist))
                AND lower(trim(other.source_name))=lower(trim(collaboration_tracks.artist)) GROUP BY other.artist_id
            ) SELECT target_id,kind,SUM(score) FROM relation_rows WHERE target_id IS NOT NULL AND target_id NOT IN (
              SELECT id FROM artists WHERE lower(trim(name)) IN ('various artists','various','unknown artist','unknown album','unknown'))
            GROUP BY target_id,kind ORDER BY SUM(score) DESC,target_id LIMIT ?
            """, c => { c.Add(id);c.Add(id);c.Add(limit*4); }))
            while (reader.Read()) relations.Add(new(reader.GetString(0),reader.GetString(1),reader.GetInt32(2)));
        var targets = relations.GroupBy(row=>row.TargetId).OrderByDescending(group=>group.Sum(row=>row.Score)).Take(limit).Select(group=>group.Key).ToHashSet();
        var related = new List<LibraryArtistDto>();
        // Preserve first-seen order for equal scores, as in the JS Map.
        foreach (var target in relations.GroupBy(row=>row.TargetId).OrderByDescending(group=>group.Sum(row=>row.Score)).Take(limit))
            if (Artist(database,target.Key) is {} node) related.Add(node);
        relations.RemoveAll(row=>!targets.Contains(row.TargetId));
        var collaborations = new List<ArtistCollaborationDto>();
        // Stream candidates; hold at most eight displayed cards, never the full ID list.
        using var candidates = CachedQuery.Reader(database, """
            SELECT DISTINCT current.track_id FROM artist_tracks current
            JOIN artist_tracks other ON other.track_id=current.track_id JOIN tracks ON tracks.id=current.track_id
            WHERE current.artist_id=? AND other.artist_id!=current.artist_id AND tracks.missing=0 AND tracks.library_hidden=0
              AND lower(trim(COALESCE(json_extract(tracks.field_sources_json,'$.artist'),''))) IN ('manual','embedded','sidecar','osu','network','technical')
            ORDER BY tracks.title COLLATE NOCASE,current.track_id
            """, c=>c.Add(id));
        while (candidates.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            if (!PersistedSortKeys.AreArtistKeysFresh(database))
                throw new UnsupportedQueryException("artist_insights_sort_keys_not_ready");
            var track = TrackByIdQuery.Execute(database,[candidates.GetString(0)]).FirstOrDefault();
            if (track is null) continue;
            var names = context.Policy.Split(track.Artist).ToArray();
            var strategy = ArtistCredit.NormalizeStrategy(searchOptions?.ArtistMergeStrategy);
            var keys = names.Select(name=>ArtistCredit.MergeKeyForName(name,strategy)).ToArray();
            if (names.Length<2 || !keys.Contains(artist.ArtistKey)) continue;
            var collaborators = new List<LibraryArtistDto>();
            for (var i=0;i<names.Length;i++)
            {
                var key = keys[i];
                if (key==artist.ArtistKey || IsGeneric(key) || names[i].ToLowerInvariant()=="various") continue;
                // Same bounded name search used by the legacy collaborator resolver.
                var search = Echo.Core.Search.LibrarySearchNormalizer.Entity(names[i],searchOptions?.ChineseCrossScriptSearchEnabled!=false);
                var filter = SqlFragments.LikeEntityFilter(["artists.name","COALESCE(artists.sort_name,'')"],search);
                using var matches = CachedQuery.Reader(database,$"""
                    SELECT artists.id FROM artists JOIN local_artist_sort_keys sort_keys ON sort_keys.artist_id=artists.id
                    WHERE {filter.Sql} AND EXISTS (SELECT 1 FROM artist_tracks JOIN tracks ON tracks.id=artist_tracks.track_id
                      WHERE artist_tracks.artist_id=artists.id AND tracks.missing=0 AND tracks.library_hidden=0)
                    ORDER BY sort_keys.name_key COLLATE NOCASE,artists.name COLLATE NOCASE LIMIT 32
                    """,c=>{foreach(var value in filter.Parameters)c.Add(value);});
                while(matches.Read())
                {
                    var match=Artist(database,matches.GetString(0));
                    if(match is not null && match.Id!=id && (match.ArtistKey==key || ArtistCredit.MergeKeyForName(match.Name,strategy)==key))
                    { if(collaborators.All(item=>item.Id!=match.Id))collaborators.Add(match);break; }
                }
            }
            if(collaborators.Count==0)continue;
            collaborations.Add(new(track,collaborators));
            if(collaborations.Count>=Math.Min(limit,8))break;
        }
        return new(artist,related,relations,collaborations);
    }
    private static bool IsGeneric(string key) => key is "variousartists" or "unknownartist" or "unknownalbum" or "unknown" or "va" or "v/a";
}
