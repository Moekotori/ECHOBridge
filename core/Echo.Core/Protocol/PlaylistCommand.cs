using System.Text.Json;

namespace Echo.Core.Protocol;

public sealed class PlaylistCommand : IRecoverableCommand
{
    public PlaylistManagementRequest? Management { get; set; }
    public string? OperationId { get; set; }
    public long IssuedAt { get; set; }
    public bool LookupOnly { get; set; }
    public string? MediaType { get; set; }
    public string? MediaId { get; set; }
    public string? FavoriteAction { get; set; }
    public string? SourceProvider { get; set; }
    public FavoriteTrackSnapshot? RemoteTrack { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement SmartInput { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement InboxInput { get; set; }
    public bool CrossScriptSearch { get; set; } = true;
    public string Operation { get; set; } = "";
    public string? PlaylistId { get; set; }
    public string? ItemId { get; set; }
    public List<string>? TrackIds { get; set; }
    public int TargetPosition { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Patch { get; set; }
    public string? BackupDirectory { get; set; }
}

public sealed record FavoriteTrackSnapshot(string Title, string Artist, string Album, double Duration, string? CoverId, string? SourceItemId, bool Unavailable);
public sealed record PlaylistCommandResult(string? PlaylistId, List<string> ItemIds, string State = "committed", bool? Liked = null, long? MatchedCount = null, string? HistoryId = null, int? AffectedCount = null, List<PlaylistRecoveryEntry>? History = null, List<PlaylistOrganization>? Organization = null, List<PlaylistGroup>? Groups = null, PlaylistInspectionPage? Inspection = null);

public sealed class PlaylistManagementRequest
{
    public string Action { get; set; } = "";
    public string? PlaylistId { get; set; }
    public List<string>? ItemIds { get; set; }
    public string? TargetPlaylistId { get; set; }
    public string? HistoryId { get; set; }
    public bool Pinned { get; set; }
    public List<string>? PlaylistIds { get; set; }
    public string? GroupId { get; set; }
    public string? Name { get; set; }
    public bool? Collapsed { get; set; }
    public long Cursor { get; set; }
    // Main-process filesystem checks, never forwarded from renderer input.
    public List<string>? MissingItemIds { get; set; }
}
public sealed record PlaylistRecoveryEntry(string Id, string PlaylistId, string Name, string Action, int Count, string CreatedAt);
public sealed record PlaylistOrganization(string PlaylistId, bool Pinned, int Position, string? GroupId = null);
public sealed record PlaylistGroup(string Id, string Name, bool Collapsed, int Position);
public sealed record PlaylistInspectionItem(string ItemId, string Title, string Artist, bool Duplicate, string Availability, string? FilePath);
public sealed record PlaylistInspectionPage(List<PlaylistInspectionItem> Items, long Cursor, bool HasMore, int Examined);
