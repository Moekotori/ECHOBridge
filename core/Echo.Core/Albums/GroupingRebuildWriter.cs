using Echo.Core.Database;
using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Albums;

internal static class GroupingRebuildWriter
{
    public static void Register(SqliteConnection connection)
    {
        AlbumGroupingStage.Register(connection);
        connection.CreateFunction<string,string>("ag_remote_dir",value=>RemoteGroupingIdentity.Directory(value,false),isDeterministic:true);
        connection.CreateFunction<string,string>("ag_remote_legacy_dir",value=>RemoteGroupingIdentity.Directory(value,true),isDeterministic:true);
        connection.CreateFunction<string,string>("ag_remote_title",RemoteGroupingIdentity.Title,isDeterministic:true);
    }
    public static LibraryMutationResult Rebuild(TransactionSql sql,GroupingRebuild request,CancellationToken cancellation)
    {
        if(request.AlbumStrategy is not ("standard" or "sameTitleAndCover") || request.ArtistStrategy is not ("standard" or "conservative")
            || request.RemoteAlbumStrategy is not ("standard" or "conservative")) throw new ArgumentException("Unknown grouping strategy.");
        if(request.NeedsAlbumRefresh)
        {
            var upgrade=sql.Value("SELECT value FROM library_maintenance_flags WHERE key=$0",AlbumIdentity.VersionKey) as string != "complete";
            if(request.VisualEvidencePath is not null) LoadEvidence(sql,request.VisualEvidencePath,cancellation);
            AlbumGroupingWriter.Rebuild(sql,request.UpdatedAt,cancellation,request.AlbumStrategy=="sameTitleAndCover");
            if(upgrade) RemoteGroupingReferences.Migrate(sql,request.RemoteAlbumStrategy,request.UpdatedAt,cancellation);
            AlbumGroupingStage.Release(sql);
        }
        GroupingArtistWriter.Rebuild(sql,request.ArtistStrategy,request.UpdatedAt,cancellation);
        cancellation.ThrowIfCancellationRequested();
        return new();
    }
    private static void LoadEvidence(TransactionSql sql,string path,CancellationToken cancellation)
    {
        using var evidence=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        evidence.Open();
        using var read=evidence.CreateCommand();
        read.CommandText="PRAGMA cache_size=-512; PRAGMA mmap_size=0";
        read.ExecuteNonQuery();
        read.CommandText="SELECT title,cover_id,match_key FROM visual_evidence ORDER BY title,cover_id";
        using var rows=read.ExecuteReader();
        sql.Run("CREATE TEMP TABLE ag_visual_evidence(title TEXT,cover_id TEXT,match_key TEXT,PRIMARY KEY(title,cover_id)) WITHOUT ROWID");
        while(rows.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            var title=rows.GetString(0); var id=rows.GetString(1); var key=rows.GetString(2);
            if(id.Length>512 || key.Length!=40 || key.Any(ch=>!Uri.IsHexDigit(ch))) throw new ArgumentException("Invalid visual cover evidence.");
            sql.Run("INSERT INTO ag_visual_evidence VALUES($0,$1,$2)",title,id,key);
        }
    }
}
