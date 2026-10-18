using System.Text;
using Echo.Core.Albums;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

// Request-owned rules; no names, library snapshots or histories survive the read.
internal sealed class ArtistReadContext : IDisposable
{
    private readonly SqliteConnection _connection;
    public GroupingArtistPolicy Policy { get; }
    public ArtistReadContext(ReadOnlyLibraryDatabase database, CoreSearchOptions? options)
    {
        _connection = database.RequireConnection();
        using (var transaction = _connection.BeginTransaction(deferred: true))
        using (var sql = new TransactionSql(_connection, transaction))
            Policy = new GroupingArtistPolicy(sql, ArtistCredit.NormalizeStrategy(options?.ArtistMergeStrategy));
        _connection.CreateFunction("echo_artist_merge_key", (string name) => Policy.Key(name));
        _connection.CreateFunction("echo_artist_search_text", (string name) => name.Normalize(NormalizationForm.FormKC).ToLowerInvariant());
    }
    public void Dispose()
    {
        _connection.CreateFunction<string, string>("echo_artist_merge_key", null);
        _connection.CreateFunction<string, string>("echo_artist_search_text", null);
    }
}
