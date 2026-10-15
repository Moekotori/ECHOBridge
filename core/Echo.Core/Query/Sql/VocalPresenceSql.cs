using System.Globalization;

namespace Echo.Core.Query.Sql;

/// <summary>
/// Instrumental detection verdict, mirrored from src/main/library/vocalDetection/vocalPresenceSql.ts and
/// src/shared/types/vocalDetection.ts: manual mark, then title/album markers, then the stored analysis score.
/// Yields 'instrumental' | 'vocal' | 'uncertain', or NULL while nothing is known.
/// </summary>
public static class VocalPresenceSql
{
    public const string Capability = "getTracks.vocalFilter";
    public const int AnalyzerVersion = 1;
    public const double InstrumentalBelow = 0.033;
    public const double VocalFrom = 0.08;

    public static readonly string[] TitleMarkers =
    [
        "instrumental", "off vocal", "off-vocal", "offvocal", "karaoke", "伴奏", "カラオケ", "纯音乐", "純音樂",
        "(inst)", "(inst.)", "[inst]", "[inst.]", "（inst）", "（inst.）", " inst.",
    ];

    public static readonly string[] AlbumMarkers = ["instrumental", "off vocal", "karaoke", "伴奏", "カラオケ"];

    private static readonly string LocalExpression = Expression("tracks.id", "tracks.title", "tracks.album");

    /// <summary>Appended to the shared track projections.</summary>
    public static readonly string LocalColumn = $",\n{LocalExpression} AS vocal_presence";

    public static string Filter(string? filter, string id, string title, string album) => filter switch
    {
        "instrumental" => $"{Expression(id, title, album)} = 'instrumental'",
        "excludeInstrumental" => $"{Expression(id, title, album)} IS NOT 'instrumental'",
        _ => string.Empty,
    };

    public static string Expression(string id, string title, string album) => $"""
        COALESCE(
          (SELECT vo.verdict FROM track_vocal_overrides vo WHERE vo.track_id = {id}),
          CASE WHEN ({Markers(title, TitleMarkers)} OR {Markers(album, AlbumMarkers)}) THEN 'instrumental' END,
          (SELECT CASE WHEN va.vocal_score < {Number(InstrumentalBelow)} THEN 'instrumental'
                       WHEN va.vocal_score >= {Number(VocalFrom)} THEN 'vocal'
                       ELSE 'uncertain' END
             FROM track_vocal_analysis va
            WHERE va.track_id = {id} AND va.analyzer_version = {AnalyzerVersion} AND va.status = 'ok')
        )
        """;

    private static string Markers(string column, string[] markers) =>
        string.Join(" OR ", markers.Select(marker => $"COALESCE({column}, '') LIKE '%{marker.Replace("'", "''")}%'"));

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
}
