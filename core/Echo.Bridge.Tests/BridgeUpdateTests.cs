using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Bridge.Windows;
using Echo.Bridge.Windows.Updates;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeUpdateTests
{
    private static readonly Version Old = new(26, 10, 15, 1), Next = new(26, 10, 15, 2);
    private static readonly string[] Required = ["ECHO Bridge.exe", "ECHO Bridge.dll", "Echo.Bridge.dll", "Echo.Core.dll",
        "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "System.Windows.Forms.dll", "e_sqlite3.dll"];

    [Fact]
    public void ReleaseSelectionIncludesPreviewsAndBothPackageNamesWithoutDowngrading()
    {
        using var releases = JsonDocument.Parse(JsonSerializer.Serialize(new[] { Release(Old), Release(new(26, 10, 15, 4), draft: true),
            Release(Next), Release(new(26, 10, 15, 3), standard: true) }));
        Assert.Equal(new Version(26, 10, 15, 3), BridgeReleaseClient.Select(releases.RootElement, Old)!.Version);
        Assert.Null(BridgeReleaseClient.Select(releases.RootElement, new(26, 10, 15, 3)));
        using var malicious = JsonDocument.Parse(JsonSerializer.Serialize(new[] { Release(Next, origin: "https://example.com") }));
        Assert.Throws<InvalidDataException>(() => BridgeReleaseClient.Select(malicious.RootElement, Old));
    }

    [Fact]
    public async Task DownloadChecksIntegrityLimitsRedirectsAndNeverDeletesAPreexistingFile()
    {
        using var fixture = new BridgeFixture(); var requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            requests++;
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent("zip"u8.ToArray()) };
        }));
        var file = Path.Combine(fixture.Root, "update.zip");
        var release = new BridgeRelease(Next, new("https://github.com/Moekotori/ECHOBridge/releases/download/v26.10.15.2/test.zip"), 3, Hash("zip"u8.ToArray()));
        var client = new BridgeReleaseClient(http, Old);
        await client.Download(release, file, default); Assert.Equal("zip", File.ReadAllText(file));
        await Assert.ThrowsAsync<IOException>(() => client.Download(release, file, default));
        Assert.Equal("zip", File.ReadAllText(file)); File.Delete(file);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.Download(release with { Sha256 = new('0', 64) }, file, default));
        Assert.False(File.Exists(file));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.Download(release with { Size = 2 }, file, default));
        Assert.False(File.Exists(file));
        using var redirects = new HttpClient(new Handler(_ => new(HttpStatusCode.Redirect)
            { Headers = { Location = new("https://example.com/payload") } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new BridgeReleaseClient(redirects, Old).Download(release, file, default));
        Assert.False(File.Exists(file)); Assert.Equal(4, requests);
    }

    [Fact]
    public void PackageVerifiesEveryFileRejectsTraversalAndChecksExpectedVersion()
    {
        using var fixture = new BridgeFixture();
        var source = Bundle(fixture.Root, "package", Next);
        var archive = Path.Combine(fixture.Root, "valid.zip"); ZipFile.CreateFromDirectory(source, archive);
        var release = Asset(archive, Next);
        var output = Path.Combine(fixture.Root, "unpacked"); BridgeUpdatePackage.Extract(archive, output, release);
        Assert.Equal(Next, BridgeUpdateManifest.Validate(output, Next).Version);
        Assert.Throws<InvalidDataException>(() => BridgeUpdatePackage.Extract(archive, Path.Combine(fixture.Root, "wrong-version"), release with { Version = Old }));
        File.WriteAllText(Path.Combine(source, Required[0]), "tampered");
        var corrupt = Path.Combine(fixture.Root, "corrupt.zip"); ZipFile.CreateFromDirectory(source, corrupt);
        Assert.Throws<InvalidDataException>(() => BridgeUpdatePackage.Extract(corrupt, Path.Combine(fixture.Root, "corrupt-out"), Asset(corrupt, Next)));
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        { using var stream = new StreamWriter(zip.CreateEntry("../escaped.txt").Open()); stream.Write("outside"); }
        Assert.Throws<InvalidDataException>(() => BridgeUpdatePackage.Extract(archive, Path.Combine(fixture.Root, "unsafe-out"), Asset(archive, Next)));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "escaped.txt")));
    }

    [Fact]
    public void ReplacementPreservesUserFilesAndAnUncommittedTransactionRestoresOldFiles()
    {
        using var fixture = new BridgeFixture();
        var target = Bundle(fixture.Root, "installed", Old); var source = Bundle(fixture.Root, "incoming", Next);
        var personal = Path.Combine(target, "my-notes.txt"); File.WriteAllText(personal, "keep");
        using (var transaction = new BridgeUpdateInstaller(source, target, Old, Next))
        { Assert.Equal(Next, BridgeUpdateManifest.Validate(target, Next).Version); }
        Assert.Equal(Old, BridgeUpdateManifest.Validate(target, Old).Version);
        using (var transaction = new BridgeUpdateInstaller(source, target, Old, Next)) transaction.Commit();
        Assert.Equal(Next, BridgeUpdateManifest.Validate(target, Next).Version);
        Assert.Equal("keep", File.ReadAllText(personal));
        Assert.Throws<InvalidDataException>(() => new BridgeUpdateInstaller(source, target, Next, Old));
    }

    [Fact]
    public void LockedOldManifestRollsBackFilesAlreadyMovedOnWindows()
    {
        using var fixture = new BridgeFixture();
        var target = Bundle(fixture.Root, "installed", Old); var source = Bundle(fixture.Root, "incoming", Next);
        using (var locked = new FileStream(Path.Combine(target, BridgeUpdateManifest.FileName), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => new BridgeUpdateInstaller(source, target, Old, Next));
        Assert.Equal(Old, BridgeUpdateManifest.Validate(target, Old).Version);
        Assert.Empty(Directory.GetDirectories(target, ".echo-bridge-backup-*"));
    }

    [Fact]
    public void DisablingAutomaticChecksPersistsWhileMissingPreferenceDefaultsOn()
    {
        using var fixture = new BridgeFixture(); var file = Path.Combine(fixture.Root, "settings.json");
        Assert.True(BridgeSettings.LoadFile(file).AutomaticUpdates);
        new BridgeSettings { AutomaticUpdates = false }.SaveFile(file);
        Assert.False(BridgeSettings.LoadFile(file).AutomaticUpdates);
    }

    private static object Release(Version version, bool draft = false, bool standard = false, string origin = BridgeReleaseClient.Repository)
    {
        var name = $"ECHO-Bridge-{version}-win-x64{(standard ? "" : "-unsigned-test")}.zip";
        return new { tag_name = "v" + version, draft, prerelease = true, assets = new[] {
            new { name, state = "uploaded", size = 3, digest = "sha256:" + new string('a', 64), browser_download_url = origin + "/releases/download/v" + version + "/" + name } } };
    }
    private static BridgeRelease Asset(string file, Version version) => new(version, new("https://github.com/Moekotori/ECHOBridge"),
        new FileInfo(file).Length, Hash(File.ReadAllBytes(file)));
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Bundle(string root, string name, Version version)
    {
        var directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
        foreach (var file in Required) File.WriteAllText(Path.Combine(directory, file), file + " " + version);
        var files = Required.Select(file => new { name = file, size = new FileInfo(Path.Combine(directory, file)).Length,
            sha256 = Hash(File.ReadAllBytes(Path.Combine(directory, file))) }).ToArray();
        File.WriteAllText(Path.Combine(directory, BridgeUpdateManifest.FileName), JsonSerializer.Serialize(new { version = version.ToString(), files }));
        return directory;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request)); }
}
