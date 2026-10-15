using System.Text.Json;

namespace Echo.Core.Database;

/// <summary>The bounded embedded catalog is loaded only for bootstrap, never cached for playback.</summary>
internal static class LibraryMigrationCatalog
{
    public static JsonDocument Load()
    {
        using var stream = typeof(LibraryMigrationCatalog).Assembly
            .GetManifestResourceStream("Echo.Core.Database.LibraryMigrations.json")
            ?? throw new InvalidOperationException("Library migration catalog is missing.");
        if (stream.Length > 1024 * 1024) throw new InvalidOperationException("Library migration catalog exceeds its budget.");
        var catalog = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
        if (catalog.RootElement.GetProperty("v").GetInt32() != 1
            || catalog.RootElement.GetProperty("migrations").GetArrayLength() > 256)
        {
            catalog.Dispose();
            throw new InvalidOperationException("Library migration catalog is invalid.");
        }
        return catalog;
    }
}
