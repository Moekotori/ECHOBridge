using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

public static class PlaylistItemsQuery
{
    internal const string ItemSelectSql = """
            SELECT
              playlist_items.id,
              playlist_items.playlist_id,
              playlist_items.media_type,
              playlist_items.media_id,
              playlist_items.source_provider,
              playlist_items.source_item_id,
              playlist_items.title_snapshot,
              playlist_items.artist_snapshot,
              playlist_items.album_snapshot,
              playlist_items.duration_snapshot,
              playlist_items.cover_id,
              playlist_items.position,
              playlist_items.added_at,
              playlist_items.added_from,
              playlist_items.unavailable,
              playlists.source_playlist_id AS playlist_source_playlist_id,
              tracks.id AS track_id,
              tracks.missing AS track_missing,
              tracks.cover_id AS track_cover_id,
              streaming_tracks.cover_url AS streaming_cover_url,
              streaming_tracks.raw_json AS streaming_raw_json,
              streaming_tracks.id AS streaming_track_id,
              streaming_tracks.stable_key AS streaming_stable_key,
              streaming_tracks.title AS streaming_title,
              streaming_tracks.artist AS streaming_artist,
              streaming_tracks.album AS streaming_album,
              streaming_tracks.album_artist AS streaming_album_artist,
              streaming_tracks.duration AS streaming_duration,
              streaming_tracks.playable AS streaming_playable,
              albums.id AS album_id,
              albums.album_key AS album_key,
              albums.title AS album_title,
              albums.album_artist AS album_artist,
              albums.year AS album_year,
              albums.track_count AS album_track_count,
              albums.duration AS album_duration,
              albums.cover_id AS album_cover_id
            FROM playlist_items
            LEFT JOIN playlists ON playlists.id = playlist_items.playlist_id
            LEFT JOIN tracks ON tracks.id = playlist_items.media_id
            LEFT JOIN streaming_tracks
              ON streaming_tracks.provider = playlist_items.source_provider
             AND streaming_tracks.provider_track_id = playlist_items.source_item_id
            LEFT JOIN albums ON albums.id = playlist_items.media_id
        """;

    public static LibraryPageDto<LibraryPlaylistItemDto> Execute(
        ReadOnlyLibraryDatabase database,
        string? playlistId,
        CoreLibraryPageQuery? query,
        CoreNormalizedEntitySearch? search,
        bool favoritePage = false,
        bool allowUnindexedTextSort = false,
        bool useRequestedSort = false)
    {
        if (!QueryCapabilities.SupportsPlaylistItems(playlistId, query))
        {
            throw new UnsupportedQueryException("getPlaylistItems combination is not migrated.");
        }

        SearchContract.EnsureExecutableEntity(query, search);
        var page = PageQueryNormalizer.Normalize(query);
        if (allowUnindexedTextSort)
        {
            using var temporary = database.RequireConnection().CreateCommand();
            temporary.CommandText = "PRAGMA temp_store=FILE";
            temporary.ExecuteNonQuery();
        }
        // Pin metadata, key revision and rows to the same SQLite snapshot.
        using var transaction = database.BeginDeferredRead();
        var playlist = ReadPlaylist(database, playlistId!);
        if (playlist.Kind == "smart")
        {
            transaction.Dispose(); // Smart pages own their read transaction.
            return Echo.Core.SmartPlaylists.SmartPlaylistQueries.Page(database,playlistId!,query);
        }
        var snapshotOrder = favoritePage || (playlist.Kind == "system" && playlist.SourcePlaylistId is "liked-tracks" or "liked-albums");
        var sort = favoritePage || useRequestedSort ? query?.Sort : playlist.SortMode;
        var needsKeys = snapshotOrder && PlaylistSnapshotOrderSql.NeedsKeys(sort);
        var useTextKeys = needsKeys && PersistedSortKeys.AreFresh(database, "local_playlist_sort_key_revision", "local_playlist_sort_keys.synced_revision");
        if (needsKeys && !useTextKeys && !allowUnindexedTextSort)
            throw new PlaylistSortKeysNotReadyException();

        var like = SqlFragments.LikeEntityFilter(
            [
                favoritePage ? "COALESCE(playlist_items.title_snapshot, tracks.title, albums.title, '')" : "COALESCE(playlist_items.title_snapshot, tracks.title, '')",
                favoritePage ? "COALESCE(playlist_items.artist_snapshot, tracks.artist, albums.album_artist, '')" : "COALESCE(playlist_items.artist_snapshot, tracks.artist, '')",
                favoritePage ? "COALESCE(playlist_items.album_snapshot, tracks.album, albums.title, '')" : "COALESCE(playlist_items.album_snapshot, tracks.album, '')",
            ],
            search);
        var parameters = new List<object?> { playlistId };
        var whereSql = "playlist_items.playlist_id = ?";
        if (favoritePage)
        {
            if (playlist.Kind != "system" || playlist.SourcePlaylistId is not ("liked-tracks" or "liked-albums"))
                throw new ArgumentException("Expected a favorite system playlist.");
            whereSql += FavoriteItemsSql.Where(playlist.SourcePlaylistId == "liked-albums", query?.SourceProvider, parameters);
        }
        if (like.Sql.Length > 0) whereSql += $" AND {like.Sql}";
        parameters.AddRange(like.Parameters.Cast<object?>());
        var orderSql = favoritePage ? FavoriteItemsSql.Order(sort, useTextKeys)
            : snapshotOrder ? PlaylistSnapshotOrderSql.For(sort, useTextKeys) : PlaylistItemsOrderSql.For(sort);
        var keyJoin = useTextKeys ? "LEFT JOIN local_playlist_sort_keys playlist_keys ON playlist_keys.item_id=playlist_items.id" : "";
        var countSql = $"""
            SELECT COUNT(*)
            FROM playlist_items
            LEFT JOIN tracks ON tracks.id = playlist_items.media_id
            LEFT JOIN albums ON albums.id = playlist_items.media_id
            WHERE {whereSql}
            """;
        var selectSql = $"""
            {ItemSelectSql}
            {keyJoin}
            WHERE {whereSql}
            {orderSql}
            LIMIT ? OFFSET ?
            """;

        var total = Convert.ToInt64(CachedQuery.Scalar(database, countSql, command => Bind(command, parameters)) ?? 0);
        var rows = new List<ItemRow>();
        using (var reader = CachedQuery.Reader(database, selectSql, command =>
        {
            Bind(command, parameters);
            command.Add(page.PageSize);
            command.Add(page.Offset);
        }))
        {
            while (reader.Read())
            {
                rows.Add(ReadRow(reader));
            }
        }

        var trackIds = rows
            .Where(static row => row.HasLocalTrack)
            .Select(static row => row.TrackId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var tracks = TrackPageQuery.HydrateTracks(database, trackIds, list: false)
            .Cast<LibraryTrackDto>()
            .ToDictionary(static track => track.Id, StringComparer.Ordinal);

        var items = rows.ConvertAll(row => MapItem(row, tracks));
        return new LibraryPageDto<LibraryPlaylistItemDto>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total,
            HasMore = page.Offset + items.Count < total,
        };
    }

    private static PlaylistMeta ReadPlaylist(ReadOnlyLibraryDatabase database, string playlistId)
    {
        const string sql = """
            SELECT kind, source_provider, source_playlist_id, sort_mode
            FROM playlists
            WHERE id = ?
            LIMIT 1
            """;
        using var reader = CachedQuery.Reader(database, sql, command => command.Add(playlistId));
        if (!reader.Read())
        {
            return new PlaylistMeta("manual", "local", null, "manual");
        }

        var kind = Text(reader, 0);
        var sort = Text(reader, 3);
        return new PlaylistMeta(
            kind is "smart" or "synced" or "system" ? kind : "manual",
            Text(reader, 1) is "netease" or "qqmusic" or "kugou" or "spotify" or "remote" ? Text(reader, 1)! : "local",
            Text(reader, 2),
            sort is "manual" or "titleAsc" or "titleDesc" or "artistAsc" or "artistDesc" or "album" or "albumDesc"
                or "artistAlbum" or "addedDesc" or "addedAsc" or "durationAsc" or "durationDesc"
                ? sort
                : "manual");
    }

    internal static ItemRow ReadRow(SqliteDataReader reader) =>
        new(
            Text(reader, 0) ?? string.Empty,
            Text(reader, 1) ?? string.Empty,
            Text(reader, 2),
            Text(reader, 3),
            Text(reader, 4),
            Text(reader, 5),
            Text(reader, 6),
            Text(reader, 7),
            Text(reader, 8),
            reader.IsDBNull(9) ? null : Convert.ToDouble(reader.GetValue(9)),
            Text(reader, 10),
            reader.IsDBNull(11) ? 0 : Convert.ToInt32(reader.GetValue(11)),
            Text(reader, 12) ?? string.Empty,
            Text(reader, 13),
            !reader.IsDBNull(14) && Convert.ToInt32(reader.GetValue(14)) != 0,
            Text(reader, 15),
            Text(reader, 16),
            !reader.IsDBNull(17) && Convert.ToInt32(reader.GetValue(17)) != 0,
            Text(reader, 18),
            Text(reader, 19),
            Text(reader, 20),
            Text(reader, 21),
            Text(reader, 22),
            Text(reader, 23),
            Text(reader, 24),
            Text(reader, 25),
            Text(reader, 26),
            reader.IsDBNull(27) ? null : Convert.ToDouble(reader.GetValue(27)),
            reader.IsDBNull(28) || Convert.ToInt32(reader.GetValue(28)) != 0,
            Text(reader, 29),
            Text(reader, 30),
            Text(reader, 31),
            Text(reader, 32),
            reader.IsDBNull(33) ? null : Convert.ToInt32(reader.GetValue(33)),
            reader.IsDBNull(34) ? 0 : Convert.ToInt32(reader.GetValue(34)),
            reader.IsDBNull(35) ? 0 : Convert.ToDouble(reader.GetValue(35)),
            Text(reader, 36));

    internal static LibraryPlaylistItemDto MapItem(ItemRow row, IReadOnlyDictionary<string, LibraryTrackDto> tracks)
    {
        var mediaType = row.MediaType is "album" or "stream_track" or "remote_file" ? row.MediaType : "track";
        var sourceProvider = row.SourceProvider is "netease" or "qqmusic" or "kugou" or "spotify" or "remote"
            ? row.SourceProvider
            : "local";
        var coverId = row.CoverId ?? row.TrackCoverId ?? row.AlbumCoverId;
        var streamingCover = CoverFromRaw(row.StreamingRawJson) ?? row.StreamingCoverUrl;
        var hasTrack = row.HasLocalTrack && tracks.ContainsKey(row.TrackId!);
        var hasStreaming = mediaType == "stream_track" && !string.IsNullOrEmpty(row.StreamingTrackId);
        var hasAlbum = !string.IsNullOrEmpty(row.AlbumId);
        var isRemoteTrack = mediaType == "track" && sourceProvider == "remote";
        var unavailable =
            (mediaType != "stream_track" && row.Unavailable)
            || (mediaType == "track" && !isRemoteTrack && (!hasTrack || string.IsNullOrEmpty(row.MediaId)))
            || (mediaType == "album" && (!hasAlbum || string.IsNullOrEmpty(row.MediaId)));

        LibraryTrackDto? track = null;
        if (hasStreaming)
        {
            var fieldSources = new Dictionary<string, string>(StringComparer.Ordinal);
            var streamingAlbumId = AlbumIdFromRaw(row.StreamingRawJson);
            if (!string.IsNullOrEmpty(streamingAlbumId))
            {
                fieldSources["streamingAlbumId"] = streamingAlbumId;
            }

            if (!string.IsNullOrEmpty(row.PlaylistSourcePlaylistId))
            {
                fieldSources["streamingSourcePlaylistId"] = row.PlaylistSourcePlaylistId;
            }

            track = new LibraryTrackDto
            {
                Id = row.StreamingStableKey ?? row.MediaId ?? row.StreamingTrackId ?? string.Empty,
                MediaType = "streaming",
                Path = row.StreamingStableKey ?? row.MediaId ?? row.StreamingTrackId ?? string.Empty,
                Provider = sourceProvider,
                ProviderTrackId = row.SourceItemId,
                StableKey = row.StreamingStableKey ?? row.MediaId,
                Title = row.StreamingTitle ?? row.TitleSnapshot ?? "Untitled",
                Artist = row.StreamingArtist ?? row.ArtistSnapshot ?? LibraryText.UnknownArtist,
                Album = row.StreamingAlbum ?? row.AlbumSnapshot ?? "Unknown Album",
                AlbumArtist = row.StreamingAlbumArtist ?? row.StreamingArtist ?? row.ArtistSnapshot ?? LibraryText.UnknownArtist,
                Duration = row.StreamingDuration ?? row.DurationSnapshot ?? 0,
                CoverId = coverId,
                CoverThumb = streamingCover,
                FieldSources = fieldSources.Count > 0 ? fieldSources : null,
                Unavailable = !row.StreamingPlayable || row.Unavailable,
            };
        }
        else if (hasTrack)
        {
            track = tracks[row.TrackId!];
        }

        return new LibraryPlaylistItemDto
        {
            Id = row.Id,
            PlaylistId = row.PlaylistId,
            MediaType = mediaType!,
            MediaId = row.MediaId,
            SourceProvider = sourceProvider!,
            SourceItemId = row.SourceItemId,
            TitleSnapshot = row.TitleSnapshot,
            ArtistSnapshot = row.ArtistSnapshot,
            AlbumSnapshot = row.AlbumSnapshot,
            DurationSnapshot = row.DurationSnapshot,
            CoverId = coverId,
            CoverThumb = !string.IsNullOrEmpty(coverId) ? LibraryText.CoverUrl(coverId, "thumb") : streamingCover,
            Position = row.Position,
            AddedAt = row.AddedAt,
            AddedFrom = row.AddedFrom,
            Unavailable = unavailable,
            Track = track,
            Album = hasAlbum
                ? new LibraryAlbumDto
                {
                    Id = row.AlbumId!,
                    AlbumKey = row.AlbumKey ?? string.Empty,
                    Title = row.AlbumTitle ?? string.Empty,
                    AlbumArtist = row.AlbumArtist ?? LibraryText.UnknownArtist,
                    Year = row.AlbumYear,
                    TrackCount = row.AlbumTrackCount,
                    Duration = row.AlbumDuration,
                    CoverId = row.AlbumCoverId,
                    CoverThumb = string.IsNullOrEmpty(row.AlbumCoverId) ? null : LibraryText.CoverUrl(row.AlbumCoverId, "album"),
                }
                : null,
        };
    }

    private static void Bind(SqliteCommand command, IReadOnlyList<object?> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Add(parameter);
        }
    }

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static string? CoverFromRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(raw);
            return document.RootElement.TryGetProperty("coverUrl", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? AlbumIdFromRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(raw);
            return document.RootElement.TryGetProperty("albumId", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private readonly record struct PlaylistMeta(string Kind, string SourceProvider, string? SourcePlaylistId, string SortMode);

    internal readonly record struct ItemRow(
        string Id,
        string PlaylistId,
        string? MediaType,
        string? MediaId,
        string? SourceProvider,
        string? SourceItemId,
        string? TitleSnapshot,
        string? ArtistSnapshot,
        string? AlbumSnapshot,
        double? DurationSnapshot,
        string? CoverId,
        int Position,
        string AddedAt,
        string? AddedFrom,
        bool Unavailable,
        string? PlaylistSourcePlaylistId,
        string? TrackId,
        bool TrackMissing,
        string? TrackCoverId,
        string? StreamingCoverUrl,
        string? StreamingRawJson,
        string? StreamingTrackId,
        string? StreamingStableKey,
        string? StreamingTitle,
        string? StreamingArtist,
        string? StreamingAlbum,
        string? StreamingAlbumArtist,
        double? StreamingDuration,
        bool StreamingPlayable,
        string? AlbumId,
        string? AlbumKey,
        string? AlbumTitle,
        string? AlbumArtist,
        int? AlbumYear,
        int AlbumTrackCount,
        double AlbumDuration,
        string? AlbumCoverId)
    {
        public bool HasLocalTrack => !string.IsNullOrEmpty(TrackId) && !TrackMissing;
    }
}
