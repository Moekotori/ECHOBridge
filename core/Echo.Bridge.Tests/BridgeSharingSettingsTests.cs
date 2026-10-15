using Echo.Bridge.Windows;
using Xunit;

namespace Echo.Bridge.Tests;

/// <summary>Default-on must never replace a user's saved off choice.</summary>
public sealed class BridgeSharingSettingsTests
{
    [Fact]
    public void NewOrMissingSharingPreferenceDefaultsToOn()
    {
        using var fixture = new BridgeFixture();
        var file = Path.Combine(fixture.Root, "bridge-settings.json");
        Assert.True(new BridgeSettings().SharingEnabled);
        Assert.True(BridgeSettings.LoadFile(file).SharingEnabled);
        Assert.False(File.Exists(file));
        File.WriteAllText(file, "{}");
        Assert.True(BridgeSettings.LoadFile(file).SharingEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedSharingChoiceSurvivesRestart(bool enabled)
    {
        using var fixture = new BridgeFixture();
        var file = Path.Combine(fixture.Root, "bridge-settings.json");
        var settings = new BridgeSettings { SharingEnabled = enabled };
        settings.SaveFile(file);
        Assert.Equal(enabled, BridgeSettings.LoadFile(file).SharingEnabled);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"SharingEnabled\":false,\"DeviceId\":\"invalid\"}")]
    public void DamagedExistingPreferenceDoesNotSilentlyResumeSharing(string json)
    {
        using var fixture = new BridgeFixture();
        var file = Path.Combine(fixture.Root, "bridge-settings.json");
        File.WriteAllText(file, json);
        Assert.False(BridgeSettings.LoadFile(file).SharingEnabled);
    }
}
