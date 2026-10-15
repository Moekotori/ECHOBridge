using System.Text.Json;
using Echo.Core.Database;
using static Echo.Core.Writes.JsonFields;
namespace Echo.Core.Writes;

internal static class ScanCoverWriter
{
    private static int Rank(string? source) => source switch { "manual"=>4,"embedded"=>3,"folder"=>2,"network"=>1,_=>0 };
    public static string Write(TransactionSql sql,JsonElement cover,string timestamp)
    {
        var hash=Text(cover,"sourceHash") ?? throw new ArgumentException("Cover hash required.");
        var id=sql.Value("SELECT id FROM covers WHERE source_hash=$0",hash) as string ?? Guid.NewGuid().ToString();
        var old=sql.Value("SELECT source_type FROM covers WHERE id=$0",id) as string;
        var source=Text(cover,"source") ?? "embedded";
        if(Rank(old)>Rank(source)) source=old!;
        sql.Run("""
            INSERT INTO covers(id,source_type,source_hash,mime_type,thumb_path,album_path,large_path,original_ref,cache_version,warnings_json,errors_json,cover_thumb,cover_large,cover_original,created_at,updated_at)
            VALUES($0,$1,$2,$3,$4,$5,$6,$7,1,$8,$9,$4,$6,$7,$10,$10)
            ON CONFLICT(id) DO UPDATE SET source_type=excluded.source_type,mime_type=excluded.mime_type,thumb_path=excluded.thumb_path,
            album_path=excluded.album_path,large_path=excluded.large_path,original_ref=excluded.original_ref,cache_version=1,warnings_json=excluded.warnings_json,
            errors_json=excluded.errors_json,cover_thumb=excluded.cover_thumb,cover_large=excluded.cover_large,cover_original=excluded.cover_original,updated_at=excluded.updated_at
            """,id,source,hash,Text(cover,"mimeType"),Text(cover,"thumbPath"),Text(cover,"albumPath"),Text(cover,"largePath"),Text(cover,"originalRef"),
            Get(cover,"warnings").ValueKind==JsonValueKind.Array ? Get(cover,"warnings").GetRawText() : "[]",
            Get(cover,"errors").ValueKind==JsonValueKind.Array ? Get(cover,"errors").GetRawText() : "[]",timestamp);
        return id;
    }
}
