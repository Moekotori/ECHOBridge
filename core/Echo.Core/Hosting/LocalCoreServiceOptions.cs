using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Core.Protocol;

namespace Echo.Core.Hosting;

/// <summary>Local-only configuration. Database/media paths are not published or logged.</summary>
public sealed class LocalCoreServiceOptions
{
    public string DirectoryPath { get; }
    public string DatabasePath { get; }
    public string Endpoint { get; }
    public bool RecoverInterruptedScans { get; }
    public DateTimeOffset? RecoverScansCreatedBefore { get; }
    internal string PipeName { get; }
    internal string DescriptorPath => Path.Combine(DirectoryPath, "service.json");

    public LocalCoreServiceOptions(string directoryPath, string databasePath, bool recoverInterruptedScans = false,
        DateTimeOffset? recoverScansCreatedBefore = null)
    {
        if (!Path.IsPathFullyQualified(directoryPath) || !Path.IsPathFullyQualified(databasePath))
            throw new InvalidOperationException("Service configuration requires absolute local paths.");
        DirectoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        DatabasePath = Path.GetFullPath(databasePath);
        RecoverInterruptedScans = recoverInterruptedScans;
        RecoverScansCreatedBefore = recoverScansCreatedBefore;
        PipeName = "echo-library-core-" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(DirectoryPath + "\n" + Environment.UserName)))[..32];
        Endpoint = OperatingSystem.IsWindows() ? @"\\.\pipe\" + PipeName : Path.Combine(DirectoryPath, "core.sock");
        if (!OperatingSystem.IsWindows() && Encoding.UTF8.GetByteCount(Endpoint) > 100)
            throw new InvalidOperationException("Service directory is too long for a local socket.");
    }

    public static LocalCoreServiceOptions FromEnvironment()
    {
        var recover = Environment.GetEnvironmentVariable("ECHO_LIBRARY_SERVICE_RECOVER_SCANS") == "1";
        var validBefore = DateTimeOffset.TryParse(Environment.GetEnvironmentVariable("ECHO_LIBRARY_SERVICE_RECOVER_SCANS_BEFORE"),
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var before);
        if (recover && !validBefore) throw new InvalidOperationException("Managed scan recovery requires a desktop startup cutoff.");
        return new(Environment.GetEnvironmentVariable("ECHO_LIBRARY_SERVICE_DIRECTORY") ?? string.Empty,
            Environment.GetEnvironmentVariable("ECHO_LIBRARY_SERVICE_DATABASE") ?? string.Empty,
            recover, validBefore ? before : null);
    }

    internal void PrepareDirectory()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DirectoryPath);
        else Directory.CreateDirectory(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if ((File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Service directory must not be a symbolic link.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(DirectoryPath) &
            (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
             UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("Service directory must be private to the current user.");
    }

    internal static FileStream AcquireLock(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Service lock must not be a symbolic link.");
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    internal void PublishDescriptor(Echo.Core.Database.LibraryBootstrapResult bootstrap)
    {
        var temporaryPath = DescriptorPath + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        // Only after both locks are held can a previous crash's temporary descriptor be removed.
        File.Delete(temporaryPath);
        using (var stream = new FileStream(temporaryPath, options))
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", 1);
            writer.WriteNumber("protocolVersion", ProtocolConstants.ProtocolVersion);
            writer.WriteString("endpoint", Endpoint);
            writer.WriteNumber("pid", Environment.ProcessId);
            writer.WriteBoolean("scanRecoveryCompleted", RecoverInterruptedScans);
            if (int.TryParse(Environment.GetEnvironmentVariable("ECHO_LIBRARY_SERVICE_PARENT_PID"), out var parentPid) && parentPid > 0)
                writer.WriteNumber("managedParentPid", parentPid);
            writer.WriteString("databaseKey", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DatabasePath))));
            writer.WriteNumber("schemaVersion", bootstrap.SchemaVersion);
            writer.WriteString("schemaDigest", bootstrap.SchemaDigest);
            writer.WriteEndObject();
        }
        File.Move(temporaryPath, DescriptorPath, overwrite: true);
    }

    internal void ValidateRequest(CoreRequest request)
    {
        if (request.Method == "handshake" && string.IsNullOrWhiteSpace(request.Params?.DatabasePath))
            throw new InvalidOperationException("Service handshake requires the configured library.");
        if (request.Params?.DatabasePath is not { } path) return;
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path), DatabasePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Service request targets a different library.");
        request.Params.DatabasePath = DatabasePath;
    }
}
