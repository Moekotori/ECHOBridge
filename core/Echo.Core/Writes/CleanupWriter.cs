using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Writes;

public static class CleanupWriter
{
    public static CleanupPage Page(ReadOnlyLibraryDatabase database,long cursor,long? ceiling)
    {
        using var transaction=database.BeginDeferredRead();
        var limit=ceiling ?? Convert.ToInt64(CachedQuery.Scalar(database,"SELECT COALESCE(MAX(rowid),0) FROM tracks") ?? 0);
        using var reader=CachedQuery.Reader(database,"SELECT rowid,id,path,size_bytes,mtime_ms,COALESCE(duration,0) FROM tracks WHERE missing=0 AND rowid>$0 AND rowid<=$1 ORDER BY rowid LIMIT 128",command=>
        {command.Parameters.AddWithValue("$0",cursor);command.Parameters.AddWithValue("$1",limit);});
        var items=new List<CleanupRow>();
        while(reader.Read()) items.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetDouble(3),reader.GetDouble(4),reader.GetDouble(5)));
        return new(items,items.Count==0 ? limit : items[^1].RowId,limit);
    }

    internal static LibraryMutationResult Delete(TransactionSql sql,List<CleanupCandidate> candidates)
    {
        sql.Run("CREATE TEMP TABLE cleanup_albums(id TEXT PRIMARY KEY)");
        var removed=0;
        var deleted=new List<string>();
        foreach(var row in candidates)
        {
            if(row.Reason is not ("missing" or "short")) throw new ArgumentException("Unknown cleanup reason.");
            if(sql.Value("SELECT 1 FROM tracks WHERE id=$0 AND path=$1 AND size_bytes=$2 AND mtime_ms=$3 AND missing=0",row.Id,row.Path,row.SizeBytes,row.MtimeMs) is null) continue;
            // A timeout/disconnected drive/permission error is not proof of deletion.
            if(row.Reason=="missing" && !ConfirmedAbsent(row.Path)) continue;
            if(row.Reason=="short" && (row.Duration<=0 || !Equals(Convert.ToDouble(sql.Value("SELECT duration FROM tracks WHERE id=$0",row.Id)),row.Duration))) continue;
            sql.Run("INSERT OR IGNORE INTO cleanup_albums SELECT album_id FROM album_tracks WHERE track_id=$0",row.Id);
            ScanMusicGraphWriter.Sync(sql,row.Id,"",default,null,DateTime.UtcNow.ToString("O"));
            var count=sql.Run("DELETE FROM tracks WHERE id=$0",row.Id);
            removed+=count;
            if(count>0) deleted.Add(row.Id);
            sql.Run("DELETE FROM album_tracks WHERE track_id=$0",row.Id);
        }
        sql.Run("""
            UPDATE albums SET track_count=(SELECT COUNT(*) FROM album_tracks a JOIN tracks t ON t.id=a.track_id WHERE a.album_id=albums.id AND t.missing=0 AND t.library_hidden=0),
            duration=COALESCE((SELECT SUM(COALESCE(t.duration,0)) FROM album_tracks a JOIN tracks t ON t.id=a.track_id WHERE a.album_id=albums.id AND t.missing=0 AND t.library_hidden=0),0),
            cover_id=(SELECT t.cover_id FROM album_tracks a JOIN tracks t ON t.id=a.track_id WHERE a.album_id=albums.id AND t.cover_id IS NOT NULL ORDER BY a.position LIMIT 1),
            updated_at=$0 WHERE id IN(SELECT id FROM cleanup_albums)
            """,DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"));
        sql.Run("""
            DELETE FROM artist_albums WHERE album_id IN(
              SELECT albums.id FROM albums
              WHERE albums.id IN(SELECT id FROM cleanup_albums)
                AND NOT EXISTS (
                  SELECT 1 FROM album_tracks a JOIN tracks t ON t.id=a.track_id
                  WHERE a.album_id=albums.id AND t.missing=0
                )
            )
            """);
        sql.Run("""
            DELETE FROM albums WHERE id IN(SELECT id FROM cleanup_albums)
              AND NOT EXISTS (
                SELECT 1 FROM album_tracks a JOIN tracks t ON t.id=a.track_id
                WHERE a.album_id=albums.id AND t.missing=0
              )
            """);
        return new(Removed:removed,Deleted:deleted);
    }

    private static bool ConfirmedAbsent(string path)
    {
        // Virtual CUE paths are not filesystem objects; their source file is.
        path=path.Split("#cueTrack=",2)[0];
        try { File.GetAttributes(path); return false; }
        catch(FileNotFoundException) { return true; }
        catch(DirectoryNotFoundException) { return true; }
        catch(IOException) { return false; }
        catch(UnauthorizedAccessException) { return false; }
    }
}
