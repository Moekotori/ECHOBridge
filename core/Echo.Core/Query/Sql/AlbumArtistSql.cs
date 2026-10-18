namespace Echo.Core.Query.Sql;

internal static class AlbumArtistSql
{
    public const string Credit = """
        CASE WHEN NULLIF(TRIM(tracks.album_artist), '') IS NOT NULL
          AND lower(COALESCE(json_extract(tracks.field_sources_json, '$.albumArtist'), ''))
            IN ('embedded', 'manual', 'network', 'sidecar', 'technical') THEN TRIM(tracks.album_artist)
          ELSE COALESCE((SELECT NULLIF(TRIM(credit_album.album_artist), '')
            FROM album_tracks AS credit_links JOIN albums AS credit_album ON credit_album.id = credit_links.album_id
            WHERE credit_links.track_id = tracks.id LIMIT 1),
            NULLIF(TRIM(tracks.album_artist), ''), NULLIF(TRIM(tracks.artist), ''), 'Unknown Artist') END
        """;

    // Reuse the writer's multilingual keys. A credit absent from all existing key
    // tables is explicitly unsupported rather than silently ordered as raw Unicode.
    public static string Grouped(bool folders, bool key) => $"""
        WITH track_credits AS (
          SELECT tracks.id AS track_id, {Credit} AS display_name,
            echo_artist_merge_key({Credit}) AS artist_key, tracks.cover_id, tracks.created_at,
            tracks.last_played_at, COALESCE(tracks.play_count,0) AS play_count,
            COALESCE(CASE WHEN album_keys.artist_text=({Credit}) THEN album_keys.artist_key END,
              CASE WHEN artist_keys.name_text=({Credit}) THEN artist_keys.name_key END,
              CASE WHEN track_keys.artist_text=({Credit}) THEN track_keys.artist_key END) AS name_key
          FROM tracks
          LEFT JOIN local_track_sort_keys track_keys ON track_keys.track_id=tracks.id AND track_keys.track_updated_at=tracks.updated_at
          LEFT JOIN local_album_sort_keys album_keys ON album_keys.album_id=(
            SELECT album_id FROM album_tracks WHERE track_id=tracks.id LIMIT 1)
            AND album_keys.album_updated_at=(SELECT updated_at FROM albums WHERE albums.id=album_keys.album_id)
          LEFT JOIN artists credit_artist ON credit_artist.artist_key=echo_artist_merge_key({Credit})
          LEFT JOIN local_artist_sort_keys artist_keys ON artist_keys.artist_id=credit_artist.id AND artist_keys.artist_updated_at=credit_artist.updated_at
          WHERE tracks.missing=0 AND tracks.library_hidden=0
            {(folders ? "AND tracks.folder_id IN (SELECT value FROM json_each(?))" : "")}
            {(key ? $"AND echo_artist_merge_key({Credit}) = ?" : "")}
        ), credit_names AS (
          SELECT artist_key,display_name,COUNT(*) AS name_weight,MIN(name_key) AS name_key FROM track_credits GROUP BY artist_key,display_name
        ), ranked_names AS (
          SELECT credit_names.*,
            ROW_NUMBER() OVER (PARTITION BY artist_key ORDER BY name_weight DESC,name_key COLLATE NOCASE,display_name COLLATE NOCASE) AS name_rank
          FROM credit_names
        ), album_grouped AS (
          SELECT track_credits.artist_key,COUNT(DISTINCT album_tracks.album_id) AS album_count,
            MIN(NULLIF(TRIM(COALESCE(albums.cover_id,track_credits.cover_id)),'')) AS cover_id
          FROM track_credits LEFT JOIN album_tracks ON album_tracks.track_id=track_credits.track_id
          LEFT JOIN albums ON albums.id=album_tracks.album_id GROUP BY track_credits.artist_key
        ), artist_grouped AS (
          SELECT artist_key,COUNT(*) AS track_count,SUM(play_count) AS play_count,
            MAX(last_played_at) AS last_played_at,MAX(created_at) AS added_at,MIN(cover_id) AS cover_id
          FROM track_credits GROUP BY artist_key
        ), grouped AS (
          SELECT artist_grouped.*,ranked_names.display_name,ranked_names.name_key,
            COALESCE(album_grouped.album_count,0) AS album_count,
            COALESCE(album_grouped.cover_id,artist_grouped.cover_id) AS grouped_cover_id
          FROM artist_grouped JOIN ranked_names ON ranked_names.artist_key=artist_grouped.artist_key AND name_rank=1
          LEFT JOIN album_grouped ON album_grouped.artist_key=artist_grouped.artist_key
        )
        """;
    public const string AvatarJoin = "LEFT JOIN artist_image_cache ON artist_image_cache.artist_key=grouped.artist_key";
    public const string Select = """
        '' AS id,grouped.artist_key,display_name AS name,display_name AS sort_name,track_count,album_count,
        grouped_cover_id AS cover_id,NULL AS cover_source,
        artist_image_cache.status AS avatar_status,artist_image_cache.provider AS avatar_provider,
        artist_image_cache.source_hash AS avatar_source_hash,artist_image_cache.thumb_path AS avatar_thumb_path,
        artist_image_cache.medium_path AS avatar_medium_path,artist_image_cache.large_path AS avatar_large_path
        """;
    public static string Order(string sort, bool avatar)
    {
        var name = "name_key COLLATE NOCASE ASC,display_name COLLATE NOCASE ASC";
        var order = sort switch
        {
            "frequent" or "trackCountDesc" => $"track_count DESC,album_count DESC,{name}",
            "trackCountAsc" => $"track_count ASC,album_count ASC,{name}",
            "albumCountDesc" => $"album_count DESC,track_count DESC,{name}",
            "lastPlayed" => $"grouped.last_played_at IS NULL,grouped.last_played_at DESC,{name}",
            "playCountDesc" => $"grouped.play_count DESC,{name}",
            "playCountAsc" => $"grouped.play_count ASC,{name}",
            "createdDesc" or "recent" => $"grouped.added_at IS NULL,grouped.added_at DESC,{name}",
            "titleDesc" => "name_key COLLATE NOCASE DESC,display_name COLLATE NOCASE DESC",
            "random" => "RANDOM()",
            _ => name,
        };
        return "ORDER BY " + (avatar ? ArtistAvatarPrioritySql.Order.Replace("avatar.", "artist_image_cache.") + "," : "") + order;
    }
}
