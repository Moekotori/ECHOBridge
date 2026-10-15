namespace Echo.Core.Albums;

internal static class RemoteGroupingIdentity
{
    private const string T = "remote_tracks";
    public static string Sql(string strategy,bool legacy=false,bool previous=false)
    {
        var directory=$"{(legacy ? "ag_remote_legacy_dir" : "ag_remote_dir")}({T}.remote_path)";
        var album=strategy=="standard" ? $"COALESCE(NULLIF(NULLIF(ag_remote_title({T}.album),''),'unknown album'),{T}.id)"
            : $"lower(trim(CASE WHEN TRIM(COALESCE({T}.album,''))='' THEN {T}.id ELSE {T}.album END))";
        var artist=$"lower(trim(COALESCE(NULLIF(TRIM({T}.album_artist),''),NULLIF(TRIM({T}.artist),''),'Unknown Artist')))";
        var year=$"COALESCE(CAST({T}.year AS TEXT),'')";
        var cover=$"NULLIF(TRIM(COALESCE(json_extract({T}.field_sources_json,'$.coverArt'),'')),'')";
        var server=$"NULLIF(TRIM(COALESCE(json_extract({T}.field_sources_json,'$.albumId'),json_extract({T}.field_sources_json,'$.serverAlbumId'),'')),'')";
        var source=$"lower(trim(COALESCE(json_extract({T}.field_sources_json,'$.albumArtist'),'')))";
        var fallback=$"({source} IN('artist_fallback','filename_fallback','unknown','missing') OR ({(legacy || previous ? "1=1" : source+" NOT IN('embedded','manual','network','sidecar','technical')")} AND lower(trim(COALESCE({T}.album_artist,'')))=lower(trim(COALESCE({T}.artist,'')))))";
        var prefix=$"{T}.source_id || char(31) || {T}.provider || char(31) || ";
        var isServer=$"{T}.provider IN('subsonic','jellyfin','emby')";
        var baseCases=$"CASE WHEN {isServer} AND {server} IS NOT NULL THEN {prefix}'server-album' || char(31) || {server} ";
        if(!legacy && !previous)
            return baseCases+$"WHEN {isServer} AND TRIM(COALESCE({T}.album,''))!='' THEN {prefix}'server-title' || char(31) || {directory} || char(31) || {album} ELSE {prefix}'folder-owner' || char(31) || {directory} || char(31) || {album} || char(31) || CASE WHEN owners.owner_count<=1 THEN '' ELSE COALESCE(credit.explicit_owner_key,matched.owner_key,'') END END";
        return baseCases+$"WHEN {isServer} AND TRIM(COALESCE({T}.album,''))!='' THEN {prefix}'server-title' || char(31) || {(!legacy || strategy=="standard" ? directory+" || char(31) || " : "")}{album}{(legacy ? " || char(31) || "+year : "")} "+
            $"WHEN NOT ({isServer}) AND {fallback} THEN {prefix}'folder-album' || char(31) || {directory} || char(31) || {album}{(legacy ? " || char(31) || "+year : "")} || {(!legacy || strategy=="standard" ? "''" : "char(31) || COALESCE("+cover+",'')")} "+
            $"ELSE {prefix}{artist} || char(31) || {(legacy ? "" : directory+" || char(31) || ")}{album}{(legacy ? " || char(31) || "+year : "")} END";
    }
    public static string Evidence=>"""
        remote_credit_base AS MATERIALIZED (
          SELECT remote_tracks.id,remote_tracks.source_id,remote_tracks.provider,
            remote_tracks.source_id||char(31)||remote_tracks.provider||char(31)||ag_remote_dir(remote_path)||char(31)||lower(trim(album)) AS credit_scope,
            NULLIF(NULLIF(ag_artist(CASE WHEN NULLIF(TRIM(album_artist),'') IS NOT NULL
              AND (lower(COALESCE(json_extract(field_sources_json,'$.albumArtist'),'')) IN('embedded','manual','network','sidecar','technical')
                OR (COALESCE(json_extract(field_sources_json,'$.albumArtist'),'')='' AND lower(trim(album_artist))!=lower(trim(COALESCE(artist,'')))))
              THEN TRIM(album_artist) END),''),'unknownartist') AS explicit_owner_key
          FROM remote_tracks JOIN remote_sources ON remote_sources.id=remote_tracks.source_id
          WHERE remote_tracks.availability!='missing' AND remote_sources.status='enabled' AND source_id=$0
        ), remote_credit_owners AS (
          SELECT credit_scope,COUNT(DISTINCT explicit_owner_key) owner_count,MIN(explicit_owner_key) owner_key
          FROM remote_credit_base GROUP BY credit_scope
        ), remote_credit_keys AS (
          SELECT DISTINCT credit_scope,explicit_owner_key owner_key FROM remote_credit_base WHERE explicit_owner_key IS NOT NULL
        )
        """;
    public static string Directory(string? value,bool legacy)
    {
        var normalized=(value??"").Replace('\\','/').TrimEnd('/');
        var index=normalized.LastIndexOf('/');
        var folder=index>0 ? AlbumIdentity.Lower(normalized[..index]) : "";
        if(legacy) return folder;
        index=folder.LastIndexOf('/');
        return AlbumIdentity.IsDiscFolder(folder[(index+1)..], allowDescription: false) ? folder[..Math.Max(0,index)] : folder;
    }
    public static string Title(string? value)
    {
        var title=AlbumIdentity.LooseTitle(value??"");
        return title.EndsWith(" single",StringComparison.Ordinal) ? title[..^7] : title;
    }
}
