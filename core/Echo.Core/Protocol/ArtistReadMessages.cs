namespace Echo.Core.Protocol;

public sealed class CoreArtistInsightsOptions
{
    public int? Limit { get; set; }
    public string? Region { get; set; }
}

public sealed record ArtistLinkedAlbumDto(string Id, string Title, int? Year, string? CoverId,
    string AlbumArtist, int PlayedTracks, int PresentTracks);
public sealed record ArtistFactsSourceDto(string ArtistId, string? Name, bool AlbumArtist,
    List<ArtistLinkedAlbumDto> Albums, int AppearanceTracks, int ComposerAlbums);
public sealed record ArtistRelationDto(string TargetId, string Kind, int Score);
public sealed record ArtistInsightsSourceDto(LibraryArtistDto? Artist, List<LibraryArtistDto> Related,
    List<ArtistRelationDto> Relations, List<ArtistCollaborationDto>? CollaborationTracks);
public sealed record ArtistCollaborationDto(LibraryTrackDto Track, List<LibraryArtistDto> Collaborators);

public sealed class CoreCatalogQuery
{
    public string Search { get; set; } = "";
    public int Limit { get; set; } = 12;
    public string? Kind { get; set; }
    public long Cursor { get; set; }
}
public sealed record CatalogPlaylistDto(string Id, string Name, string? Description, int ItemCount,
    string? CoverId, string? CoverThumb);
public sealed record CatalogFolderDto(string Id, string Name, string Path);
public sealed record CatalogCandidateDto(long Cursor, string Id, string Name);
public sealed record CatalogSourceDto(List<CatalogPlaylistDto> Playlists, List<CatalogFolderDto> Folders,
    List<CatalogCandidateDto>? Candidates = null);
