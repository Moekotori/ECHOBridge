using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query;
using Microsoft.Data.Sqlite;

namespace Echo.Core.SmartPlaylists;

public sealed record SmartPreviewResult(List<LibraryTrackDto> Items, long CandidateCount, long IncludedCount, int RequestedLimit);

internal static class SmartPlaylistQueries
{
    public static SmartPreviewResult Preview(ReadOnlyLibraryDatabase database, JsonElement input)
    {
        var rules = SmartRuleNormalizer.Normalize(SmartRuleValues.Object(input));
        using var transaction = database.BeginDeferredRead();
        var query = SmartRuleSql.Build(rules,DateTimeOffset.UtcNow);
        var count = Count(database,query);
        var limit = (int)SmartRuleValues.Number(rules["limit"])!;
        return new(Tracks(database,query,Math.Min(8,limit),0),count,Math.Min(count,limit),limit);
    }
    public static void Enrich(ReadOnlyLibraryDatabase database, LibraryPlaylistDto playlist)
    {
        if (playlist.Kind != "smart") return;
        var rules = Rules(database,playlist.Id);
        if (rules is null) { playlist.ItemCount = 0; return; }
        playlist.SmartRules = SmartRuleValues.Element(SmartRuleNormalizer.Normalize(new JsonObject { ["rules"] = rules.DeepClone() }));
        var query = SmartRuleSql.Build(rules,DateTimeOffset.UtcNow);
        playlist.ItemCount = (int)Math.Min(Count(database,query),SmartRuleValues.Number(rules["limit"])!.Value);
        if (playlist.CoverId is not null || playlist.CoverThumb is not null) return;
        var parameters = new List<object?>(query.Parameters);
        var firstId = SmartPlaylistOrder.SelectIds(query, rules, 1, 0, parameters);
        using var command = Command(database,$"SELECT cover_id FROM tracks WHERE id = ({firstId})",parameters);
        var cover = command.ExecuteScalar() as string;
        playlist.CoverThumb = cover is null ? null : Echo.Core.Mapping.LibraryText.CoverUrl(cover,"album");
    }
    public static LibraryPageDto<LibraryPlaylistItemDto> Page(ReadOnlyLibraryDatabase database,string id,CoreLibraryPageQuery? input)
    {
        using var transaction = database.BeginDeferredRead();
        var page = PageQueryNormalizer.Normalize(input);
        var rules = Rules(database,id);
        if (rules is null) return new() { Page=page.Page,PageSize=page.PageSize,Items=[],Total=0,HasMore=false };
        var query = SmartRuleSql.Build(rules,DateTimeOffset.UtcNow,input?.Search);
        var count = Math.Min(Count(database,query),(int)SmartRuleValues.Number(rules["limit"])!);
        var tracks = Tracks(database,query,(int)Math.Max(0,Math.Min(page.PageSize,count-page.Offset)),page.Offset,rules);
        using var createdCommand = Command(database,"SELECT created_at FROM playlists WHERE id=$0",[id]);
        var created = createdCommand.ExecuteScalar() as string ?? "";
        var items = tracks.Select((track,index) => new LibraryPlaylistItemDto {
            Id=$"smart:{id}:{track.Id}",PlaylistId=id,MediaType="track",MediaId=track.Id,SourceProvider="local",SourceItemId=track.Path,
            TitleSnapshot=track.Title,ArtistSnapshot=track.Artist,AlbumSnapshot=track.Album,DurationSnapshot=track.Duration,
            CoverId=track.CoverId,CoverThumb=track.CoverThumb,Position=page.Offset+index,AddedAt=track.CreatedAt ?? created,
            AddedFrom="smart-rule",Unavailable=track.Unavailable,Track=track,Album=null,
        }).ToList();
        return new() { Items=items,Total=count,Page=page.Page,PageSize=page.PageSize,HasMore=page.Offset+items.Count<count };
    }
    private static JsonObject? Rules(ReadOnlyLibraryDatabase database,string id)
    {
        using var command = Command(database,"SELECT rules_json FROM smart_playlist_rules WHERE playlist_id=$0",[id]);
        if (command.ExecuteScalar() is not string json) return null;
        return SmartPlaylistOrder.Normalize(JsonNode.Parse(json)!.AsObject());
    }
    private static long Count(ReadOnlyLibraryDatabase database,SmartQuery query)
    {
        using var command = Command(database,$"SELECT COUNT(*) FROM tracks WHERE {query.Where}",query.Parameters);
        return Convert.ToInt64(command.ExecuteScalar());
    }
    private static List<LibraryTrackDto> Tracks(ReadOnlyLibraryDatabase database,SmartQuery query,int count,int offset,JsonObject? rules = null)
    {
        if (count == 0) return [];
        var ids = new List<string>();
        var parameters = new List<object?>(query.Parameters);
        var sql = rules is null ? $"SELECT tracks.id FROM tracks WHERE {query.Where} ORDER BY {query.Order} LIMIT {count} OFFSET {offset}"
            : SmartPlaylistOrder.SelectIds(query, rules, count, offset, parameters);
        using (var command = Command(database,sql,parameters))
        using (var reader = command.ExecuteReader()) { while (reader.Read()) ids.Add(reader.GetString(0)); }
        return TrackPageQuery.HydrateTracks(database,ids,list:false).Cast<LibraryTrackDto>().ToList();
    }
    private static SqliteCommand Command(ReadOnlyLibraryDatabase database,string sql,List<object?> values)
    {
        var command = database.RequireConnection().CreateCommand();
        command.CommandText=sql;
        for(var i=0;i<values.Count;i++) command.Parameters.AddWithValue("$"+i,values[i] ?? DBNull.Value);
        return command;
    }
}
