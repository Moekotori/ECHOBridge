using Echo.Core.Database;
using Echo.Core.Protocol;
using System.Text;

namespace Echo.Core.Writes;

internal static class ScanArtistRefreshWriter
{
    public const string Capability = "library.scanArtistRefresh/v1";
    public const string InlineCapability = "library.scanArtistRefreshInline/v1";
    public static LibraryMutationResult Enqueue(TransactionSql sql, ScanArtistRefresh request)
    {
        if (request.TrackIds.Count + request.AlbumIds.Count > 128
            || string.IsNullOrWhiteSpace(request.CreatedAt) || request.CreatedAt.Length > 64
            || request.TrackIds.Concat(request.AlbumIds).Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 512))
            throw new ArgumentException("Invalid artist refresh batch.");
        if (request.TrackIds.Count + request.AlbumIds.Count == 0) return new();
        var statement = new StringBuilder("INSERT OR IGNORE INTO artist_index_refresh_queue (entity_kind, entity_id, created_at) VALUES ");
        var values = new List<object?> { request.CreatedAt };
        void Add(string kind, IEnumerable<string> ids)
        {
            foreach (var id in ids)
            {
                if (values.Count > 1) statement.Append(',');
                statement.Append("('").Append(kind).Append("', $").Append(values.Count).Append(", $0)");
                values.Add(id);
            }
        }
        Add("track", request.TrackIds);
        Add("album", request.AlbumIds);
        sql.Run(statement.ToString(), values.ToArray());
        return new();
    }
}
