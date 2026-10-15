using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Bridge.Library;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeFolderCatalogTests
{
    [Fact]
    public void EveryDirectorySortIsAppliedBeforePagingAndDoesNotExposePathsOrWriteLibrary()
    {
        using var fixture = new BridgeFixture();
        var sql = new StringBuilder("BEGIN;");
        for (var i = 1; i <= 57; i++)
        {
            var name = $"Node-{i:00}";
            for (var j = 1; j <= i; j++)
            {
                var id = $"track-{i}-{j}";
                var path = Path.Combine(fixture.Root, name, $"{j}.wav").Replace("'", "''");
                var duration = ((1000d - i) / i).ToString("R", CultureInfo.InvariantCulture);
                sql.Append($"""
                    INSERT INTO tracks(id,path,folder_id,size_bytes,mtime_ms,title,artist,album,album_artist,field_sources_json,created_at,updated_at,duration)
                    VALUES('{id}','{path}','folder',{(j == 1 ? 10000 - i : 0)},{1000 - i},'{id}','Artist','Album','Artist','{"{}"}','before','before',{duration});
                    INSERT OR IGNORE INTO albums(id,album_key,title,album_artist,created_at,updated_at)
                    VALUES('album-{j % 7}','album-{j % 7}','Album','Artist','before','before');
                    INSERT INTO album_tracks(album_id,track_id) VALUES('album-{j % 7}','{id}');
                    """);
            }
        }
        sql.Append("COMMIT;"); fixture.Execute(sql.ToString());
        var hash = SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        var catalog = new BridgeCatalog(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)));
        var names = Enumerable.Range(1, 57).Select(i => $"Node-{i:00}").ToArray();
        foreach (var sort in new[] { "titleAsc", "trackCountDesc", "albumCountDesc", "durationDesc", "sizeDesc", "fileModifiedDesc" })
        {
            var expected = sort switch
            {
                "trackCountDesc" => names.Reverse().ToArray(),
                "albumCountDesc" => names.OrderByDescending(name => Math.Min(7, int.Parse(name[5..]))).ThenBy(name => name).ToArray(),
                _ => names,
            };
            var first = Read(catalog, 1, "root:folder", sort);
            var second = Read(catalog, 2, "root:folder", sort);
            Assert.Equal(57, first.GetProperty("total").GetInt64());
            Assert.Equal(50, first.GetProperty("entries").GetArrayLength());
            Assert.Equal(expected, first.GetProperty("entries").EnumerateArray().Concat(second.GetProperty("entries").EnumerateArray())
                .Select(row => row.GetProperty("title").GetString()).ToArray());
            Assert.DoesNotContain(fixture.Root, first.ToString());
        }
        var node = Read(catalog, 1, "root:folder", "trackCountDesc").GetProperty("entries")[0].GetProperty("id").GetString()!;
        Assert.StartsWith("node:", node);
        Assert.Equal(0, Read(catalog, 1, node, "titleAsc").GetProperty("total").GetInt64());
        Assert.Equal(57, Read(catalog, 3, "root:folder", "titleAsc").GetProperty("total").GetInt64());
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)));
    }

    [Fact]
    public void RootDirectoriesAreSortedBeforePagingIncludingEmptyRootsAndSearch()
    {
        using var fixture = new BridgeFixture();
        var sql = new StringBuilder("BEGIN;");
        for (var i = 60; i >= 1; i--)
            sql.Append($"INSERT INTO folders(id,path,name,created_at,updated_at) VALUES('root-{i:00}',$root||'/{i}','Root-{i:00}','before','before');");
        sql.Append("COMMIT;"); fixture.Execute(sql.ToString());
        var catalog = new BridgeCatalog(new BridgeLibraryReader(BridgeProfile.Open(fixture.DatabasePath)));
        var second = Read(catalog, 2, null, "titleAsc", "Root-");
        Assert.Equal(60, second.GetProperty("total").GetInt64());
        Assert.Equal("Root-51", second.GetProperty("entries")[0].GetProperty("title").GetString());
        var byCount = Read(catalog, 1, null, "trackCountDesc");
        Assert.Equal("Fixture", byCount.GetProperty("entries")[0].GetProperty("title").GetString());
        Assert.Throws<BridgeException>(() => catalog.Page("folders", 1, "", "unsupported", null, null, default));
    }

    private static JsonElement Read(BridgeCatalog catalog, int page, string? parent, string sort, string search = "") =>
        JsonSerializer.SerializeToElement(catalog.Page("folders", page, search, sort, parent is null ? null : "folder", parent, default));
}
