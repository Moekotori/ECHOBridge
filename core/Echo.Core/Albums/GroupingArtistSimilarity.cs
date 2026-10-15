using Echo.Core.Database;

namespace Echo.Core.Albums;

internal static class GroupingArtistSimilarity
{
    public static string Resolve(TransactionSql sql,string key,string? trackId,string? albumId,CancellationToken cancellation)
    {
        var best=key; var bestScore=0d;
        using var read=sql.Command("""
            SELECT artist_key,
              EXISTS(SELECT 1 FROM ag_artist_tracks WHERE artist_id=stats.id AND track_id=$1)
                OR EXISTS(SELECT 1 FROM ag_artist_albums WHERE artist_id=stats.id AND album_id=$2) AS overlap
            FROM ag_artist_stats stats WHERE length(artist_key)>=7
              AND (substr(artist_key,1,3)=substr($0,1,3) OR substr(artist_key,-3)=substr($0,-3))
            ORDER BY (substr(artist_key,1,3)=substr($0,1,3)) DESC,ordinal
            """,key,trackId,albumId);
        using var reader=read.ExecuteReader();
        while(reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            var candidate=reader.GetString(0); var length=Math.Max(key.Length,candidate.Length);
            var limit=Math.Max(1,(int)Math.Floor(length*.12));
            var distance=Distance(key,candidate,limit);
            var score=distance>limit ? 0d : 1d-(double)distance/length;
            if(score>bestScore && score>=(reader.GetBoolean(1) ? .93 : .96)) { best=candidate; bestScore=score; }
        }
        return best;
    }
    private static int Distance(string left,string right,int limit)
    {
        if(Math.Abs(left.Length-right.Length)>limit) return limit+1;
        var previous=Enumerable.Range(0,right.Length+1).ToArray(); var current=new int[right.Length+1];
        for(var i=1;i<=left.Length;i++)
        {
            current[0]=i; var minimum=i;
            for(var j=1;j<=right.Length;j++)
            {
                current[j]=Math.Min(previous[j]+1,Math.Min(current[j-1]+1,previous[j-1]+(left[i-1]==right[j-1]?0:1)));
                minimum=Math.Min(minimum,current[j]);
            }
            if(minimum>limit) return limit+1;
            (previous,current)=(current,previous);
        }
        return previous[right.Length];
    }
}
