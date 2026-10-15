using Echo.Core.Database;
using Microsoft.Data.Sqlite;

namespace Echo.Bridge.Tests;

internal sealed class BridgeFixture : IDisposable
{
    public string Root { get; }
    public string DatabasePath => Path.Combine(Root, "echo-library.sqlite");
    public string MediaPath => Path.Combine(Root, "source.wav");
    public BridgeFixture()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "package.json"))) repository = repository.Parent;
        Root = Path.Combine(repository!.FullName, "misc", "bridge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        File.WriteAllBytes(MediaPath, Enumerable.Range(0, 1024).Select(number => (byte)(number % 256)).ToArray());
        LibraryDatabaseBootstrap.Initialize(DatabasePath);
        Execute("""
            INSERT INTO folders(id,path,name,created_at,updated_at) VALUES('folder',$root,'Fixture','before','before');
            INSERT INTO tracks(id,path,folder_id,size_bytes,mtime_ms,title,artist,album,album_artist,field_sources_json,created_at,updated_at,duration,codec,sample_rate,bit_depth)
            VALUES('kept',$media,'folder',1024,1,'Preserved','Artist','Album','Artist','{}','before','before',12.5,'pcm',44100,16);
            """);
    }
    public void Execute(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql;
        command.Parameters.AddWithValue("$root", Root); command.Parameters.AddWithValue("$media", MediaPath); command.ExecuteNonQuery();
    }
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
