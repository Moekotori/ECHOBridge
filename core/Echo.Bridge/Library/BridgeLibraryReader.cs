using Echo.Core.Hosting;
using Echo.Core.Protocol;

namespace Echo.Bridge.Library;

/// <summary>The existing Core query implementation, with an explicit read-only method boundary.</summary>
public sealed class BridgeLibraryReader(BridgeProfile profile)
{
    private static readonly HashSet<string> Reads = ["getTracks", "getAlbums", "getArtists", "getGenres",
        "getAlbumTracks", "getArtistTracks", "getArtistAlbums", "getGenreTracks", "getTracksByIds",
        "getPlaylists", "getPlaylistItems", "getFavoriteItems", "getFolderOverviews", "getFolderChildren", "getFolderTracks"];

    public BridgeProfile Profile { get; } = profile;

    public object? Read(string method, CoreRequestParams? parameters = null, CancellationToken cancellation = default)
    {
        if (!Reads.Contains(method)) throw new BridgeException(405, "library_read_only");
        cancellation.ThrowIfCancellationRequested();
        using var executor = new RequestExecutor(databaseBudgetParts: 1, allowUnindexedTextSort: true, useRequestedPlaylistSort: true);
        using var interrupt = cancellation.Register(executor.InterruptActiveRead);
        executor.Execute(new CoreRequest { Method = "handshake", DatabaseGeneration = 1,
            Params = new() { DatabasePath = Profile.DatabasePath, DatabaseGeneration = 1 }, Cancellation = cancellation });
        parameters ??= new();
        parameters.SearchOptions = Profile.SearchOptions;
        parameters.NormalizeSearch = true;
        // Dispose closes SQLite after every request: no idle connection, pool, migration or scan owner.
        var result = executor.Execute(new CoreRequest { Method = method, DatabaseGeneration = 1,
            Params = parameters, Cancellation = cancellation });
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    public LibraryTrackDto Track(string id, CancellationToken cancellation = default)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 1024) throw new BridgeException(400, "invalid_peer_track");
        return ((IEnumerable<LibraryTrackDto>)Read("getTracksByIds", new() { TrackIds = [id] }, cancellation)!)
            .FirstOrDefault() ?? throw new BridgeException(404, "peer_track_not_found");
    }
}
