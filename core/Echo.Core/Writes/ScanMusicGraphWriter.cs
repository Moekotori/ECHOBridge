using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Echo.Core.Database;
using static Echo.Core.Writes.JsonFields;

namespace Echo.Core.Writes;

internal static class ScanMusicGraphWriter
{
    private static string? Clean(string? value)
    {
        if(value is null) return null;
        value=Regex.Replace(value.Normalize(NormalizationForm.FormKC).Trim(),@"\s+"," ");
        return value.Length is >0 and <=512 ? value : null;
    }
    private static string Key(string? value) => Clean(value)?.ToLowerInvariant() ?? "";
    private static string Id(string prefix,string key) => prefix+":"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()[..24];
    private static List<string> Names(JsonElement g,string field)
    {
        var names=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var value in Array(g,field)) if(Clean(value.GetString()) is { } name) names[Key(name)]=name;
        return names.Values.ToList();
    }
    private static IEnumerable<JsonElement> Array(JsonElement g,string field)
    {
        var values=Get(g,field);
        if(values.ValueKind!=JsonValueKind.Array) return [];
        if(values.GetArrayLength()>512) throw new ArgumentException("Music credits exceed the per-track bound.");
        return values.EnumerateArray();
    }
    private static List<string> Previous(TransactionSql sql,string query,string id)
    {
        using var command=sql.Command(query,id); using var reader=command.ExecuteReader();
        var result=new List<string>();
        while(reader.Read()) { if(result.Count>=2048) throw new ArgumentException("Existing music credits exceed the mutation bound."); if(!reader.IsDBNull(0)) result.Add(reader.GetString(0)); }
        return result;
    }

    public static void Sync(TransactionSql sql,string id,string title,JsonElement g,string? fingerprint,string timestamp)
    {
        var work=sql.Value("SELECT work_id FROM track_music_graph WHERE track_id=$0",id) as string;
        var movement=sql.Value("SELECT movement_id FROM track_music_graph WHERE track_id=$0",id) as string;
        var performance=sql.Value("SELECT performance_id FROM track_music_graph WHERE track_id=$0",id) as string;
        var labels=Previous(sql,"SELECT label_id FROM music_track_labels WHERE track_id=$0",id);
        var people=Previous(sql,"SELECT DISTINCT person_id FROM music_credits WHERE track_id=$0",id);
        var roles=Previous(sql,"SELECT DISTINCT role_id FROM music_credits WHERE track_id=$0",id);
        sql.Run("DELETE FROM music_credits WHERE track_id=$0",id);
        sql.Run("DELETE FROM music_track_labels WHERE track_id=$0",id);
        sql.Run("DELETE FROM track_music_graph WHERE track_id=$0",id);
        if(performance is not null) sql.Run("DELETE FROM music_performances WHERE id=$0",performance);
        var hasMetadata=g.ValueKind==JsonValueKind.Object && (Clean(Text(g,"work")) is not null || Clean(Text(g,"movement")) is not null
            || new[]{"composers","conductors","performers","labels","credits"}.Any(field=>Array(g,field).Any()));
        if(hasMetadata) WriteGraph(sql,id,title,g,fingerprint,timestamp);
        if(movement is not null) sql.Run("DELETE FROM music_movements WHERE id=$0 AND NOT EXISTS(SELECT 1 FROM track_music_graph WHERE movement_id=$0) AND NOT EXISTS(SELECT 1 FROM music_performances WHERE movement_id=$0)",movement);
        if(work is not null) sql.Run("DELETE FROM music_works WHERE id=$0 AND NOT EXISTS(SELECT 1 FROM track_music_graph WHERE work_id=$0) AND NOT EXISTS(SELECT 1 FROM music_movements WHERE work_id=$0) AND NOT EXISTS(SELECT 1 FROM music_performances WHERE work_id=$0)",work);
        foreach(var value in labels) sql.Run("DELETE FROM music_labels WHERE id=$0 AND NOT EXISTS(SELECT 1 FROM music_track_labels WHERE label_id=$0)",value);
        foreach(var value in people) sql.Run("DELETE FROM music_people WHERE id=$0 AND NOT EXISTS(SELECT 1 FROM music_credits WHERE person_id=$0)",value);
        foreach(var value in roles) sql.Run("DELETE FROM music_credit_roles WHERE id=$0 AND NOT EXISTS(SELECT 1 FROM music_credits WHERE role_id=$0)",value);
    }

    private static void WriteGraph(TransactionSql sql,string track,string title,JsonElement g,string? fingerprint,string timestamp)
    {
        var composers=Names(g,"composers");
        string? work=null,movement=null;
        if(Clean(Text(g,"work")) is { } workTitle)
        {
            var key=Key(workTitle)+"\0"+string.Join('\x01',composers.Select(Key).Order(StringComparer.Ordinal));
            sql.Run("INSERT INTO music_works VALUES($0,$1,$2,$3,$3) ON CONFLICT(work_key) DO UPDATE SET title=excluded.title,updated_at=excluded.updated_at",Id("work",key),key,workTitle,timestamp);
            work=(string?)sql.Value("SELECT id FROM music_works WHERE work_key=$0",key);
        }
        if(Clean(Text(g,"movement")) is { } movementTitle)
        {
            var key=(work ?? "track:"+track)+"\0"+(Number(g,"movementNo")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "")+"\0"+Key(movementTitle);
            sql.Run("""
                INSERT INTO music_movements(id,movement_key,work_id,title,movement_no,movement_total,created_at,updated_at) VALUES($0,$1,$2,$3,$4,$5,$6,$6)
                ON CONFLICT(movement_key) DO UPDATE SET title=excluded.title,movement_no=excluded.movement_no,movement_total=excluded.movement_total,updated_at=excluded.updated_at
                """,Id("movement",key),key,work,movementTitle,Number(g,"movementNo"),Number(g,"movementTotal"),timestamp);
            movement=(string?)sql.Value("SELECT id FROM music_movements WHERE movement_key=$0",key);
        }
        var performanceKey="track:"+track;
        sql.Run("""
            INSERT INTO music_performances(id,performance_key,work_id,movement_id,title,performance_date,venue,is_synthetic,created_at,updated_at)
            VALUES($0,$1,$2,$3,$4,NULL,NULL,1,$5,$5) ON CONFLICT(performance_key) DO UPDATE SET work_id=excluded.work_id,movement_id=excluded.movement_id,title=excluded.title,updated_at=excluded.updated_at
            """,Id("performance",performanceKey),performanceKey,work,movement,Clean(Text(g,"movement")) ?? Clean(Text(g,"work")) ?? Clean(title) ?? "Performance",timestamp);
        var performance=(string)sql.Value("SELECT id FROM music_performances WHERE performance_key=$0",performanceKey)!;
        // Fingerprinting remains in the existing normalization adapter so serialized
        // Unicode/array ordering stays identical across JS and .NET runtimes.
        var source=Text(g,"source");
        sql.Run("INSERT INTO track_music_graph VALUES($0,$1,$2,$3,$4,$5)",track,work,movement,performance,JsonSerializer.Serialize(new Dictionary<string,string?>{{"source",source},{"fingerprint",fingerprint}}),timestamp);
        var position=0;
        foreach(var name in Names(g,"labels"))
        {
            var key=Key(name);
            sql.Run("INSERT INTO music_labels VALUES($0,$1,$2,$3,$3) ON CONFLICT(label_key) DO UPDATE SET name=excluded.name,updated_at=excluded.updated_at",Id("label",key),key,name,timestamp);
            sql.Run("INSERT INTO music_track_labels VALUES($0,$1,$2)",track,sql.Value("SELECT id FROM music_labels WHERE label_key=$0",key),position++);
        }
        var credits=composers.Select(name=>(name,"composer",(string?)null))
            .Concat(Names(g,"conductors").Select(name=>(name,"conductor",(string?)null)))
            .Concat(Array(g,"performers").Concat(Array(g,"credits")).Select(c=>(Text(c,"name") ?? "",Text(c,"role") ?? "",Text(c,"instrument"))));
        var seen=new HashSet<string>(StringComparer.Ordinal); position=0;
        foreach(var (rawName,rawRole,rawInstrument) in credits)
        {
            var name=Clean(rawName); var role=Key(rawRole).Replace(' ','-'); var instrument=Clean(rawInstrument);
            if(name is null || role.Length==0 || !seen.Add(Key(name)+"\0"+role+"\0"+Key(instrument))) continue;
            var personKey=Key(name);
            sql.Run("INSERT INTO music_people VALUES($0,$1,$2,NULL,$3,$3) ON CONFLICT(person_key) DO UPDATE SET name=excluded.name,updated_at=excluded.updated_at",Id("person",personKey),personKey,name,timestamp);
            var person=(string)sql.Value("SELECT id FROM music_people WHERE person_key=$0",personKey)!;
            var category=role switch { "composer" or "lyricist" or "writer" or "arranger"=>"composition","conductor" or "performer"=>"performance","producer" or "engineer" or "mixer" or "remixer" or "dj-mixer" or "technician"=>"production",_=>"other" };
            var roleName=role=="dj-mixer" ? "DJ Mixer" : Regex.Replace(role.Replace('-',' '),@"\b\p{L}",m=>m.Value.ToUpperInvariant());
            sql.Run("INSERT INTO music_credit_roles VALUES($0,$1,$2,$3,$4,$4) ON CONFLICT(role_key) DO UPDATE SET name=excluded.name,category=excluded.category,updated_at=excluded.updated_at",Id("role",role),role,roleName,category,timestamp);
            var roleId=(string)sql.Value("SELECT id FROM music_credit_roles WHERE role_key=$0",role)!;
            var targetType=role=="composer" && work is not null ? "work" : role is "conductor" or "performer" ? "performance" : "track";
            var target=targetType=="work" ? work! : targetType=="performance" ? performance : track;
            var creditId=Id("credit",string.Join('\0',track,person,roleId,targetType,target,Key(instrument)));
            sql.Run("INSERT INTO music_credits VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$10)",creditId,track,person,roleId,targetType,target,name,instrument,position++,source,timestamp);
        }
    }
}
