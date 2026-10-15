using System.Text.Json;
using Echo.Bridge.Library;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeProfileDiscoveryTests
{
    [Fact]
    public void RememberedLibraryRemainsReadableWithoutARunningProducerOrLinkSession()
    {
        using var fixture = new BridgeFixture();
        var hintDirectory = Path.Combine(fixture.Root, "ECHO Bridge"); Directory.CreateDirectory(hintDirectory);
        File.WriteAllBytes(Path.Combine(hintDirectory, "echo-library-profile.json"),
            JsonSerializer.SerializeToUtf8Bytes(new { version = 1, databasePath = fixture.DatabasePath, loadedAt = "2026-01-01T00:00:00Z" }));
        var selected = BridgeProfileDiscovery.Automatic(fixture.Root);
        Assert.Equal(fixture.DatabasePath, selected);
        Assert.Equal("Preserved", new BridgeLibraryReader(BridgeProfile.Open(selected!)).Track("kept").Title);
    }

    [Fact]
    public void ExactLoadedLibraryWinsAndStaleOrMalformedHintsFallBackWithoutCreatingDatabases()
    {
        using var fixture = new BridgeFixture();
        var regular = Path.Combine(fixture.Root, "ECHO"); Directory.CreateDirectory(regular);
        var standard = Path.Combine(regular, "echo-library.sqlite"); File.WriteAllText(standard, "existing");
        var alternative = Path.Combine(fixture.Root, "ECHO NEXT"); Directory.CreateDirectory(alternative);
        File.WriteAllText(Path.Combine(alternative, "echo-library.sqlite"), "existing");
        File.WriteAllText(Path.Combine(regular, "echo-settings.json"), "{}");
        var hintDirectory = Path.Combine(fixture.Root, "ECHO Bridge"); Directory.CreateDirectory(hintDirectory);
        var hint = Path.Combine(hintDirectory, "echo-library-profile.json");
        Assert.Equal(standard, BridgeProfileDiscovery.Automatic(fixture.Root));
        File.WriteAllBytes(hint, JsonSerializer.SerializeToUtf8Bytes(new { version = 1, databasePath = fixture.DatabasePath }));
        Assert.Equal(fixture.DatabasePath, BridgeProfileDiscovery.Automatic(fixture.Root));
        var missing = Path.Combine(fixture.Root, "missing.sqlite");
        File.WriteAllBytes(hint, JsonSerializer.SerializeToUtf8Bytes(new { version = 1, databasePath = missing }));
        Assert.Equal(standard, BridgeProfileDiscovery.Automatic(fixture.Root)); Assert.False(File.Exists(missing));
        File.WriteAllText(hint, "{"); Assert.Equal(standard, BridgeProfileDiscovery.Automatic(fixture.Root));
        File.WriteAllText(hint, new string('x', 17 * 1024)); Assert.Equal(standard, BridgeProfileDiscovery.Automatic(fixture.Root));
    }
}
