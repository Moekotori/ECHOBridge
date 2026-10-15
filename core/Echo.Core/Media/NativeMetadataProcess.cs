using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Echo.Core.Protocol;

namespace Echo.Core.Media;

/// <summary>Owns one idle-releasable scanner process on the dedicated media lane.</summary>
public sealed class NativeMetadataProcess : IDisposable
{
    public const string Capability = "media.nativeMetadata/v1";
    public const string CancellationCapability = "media.nativeMetadataCancellation/v1";
    // Large-file header scans outlive 10s. Stay under the Node readNativeMetadata deadline (70s).
    private const int DefaultTimeoutMs = 60_000;
    private const int MinTimeoutMs = 1_000;
    private const int MaxLineChars = 262_144;
    // The scanner's small JSON parser accepts UTF-8 text but not \u escapes.
    private static readonly JsonSerializerOptions ScannerJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private Process? _process;
    private WindowsScannerProcessOwnership? _ownership;
    private string? _scannerPath;
    private long _requestId;
    private bool _stopping;
    private readonly Func<Process, (WindowsScannerProcessOwnership? Ownership, int Error)> _attachOwnership;

    public NativeMetadataProcess() : this(static process => {
        var ownership = WindowsScannerProcessOwnership.TryAttach(process, out var error);
        return (ownership, error);
    }) { }

    internal NativeMetadataProcess(Func<Process, (WindowsScannerProcessOwnership? Ownership, int Error)> attachOwnership) =>
        _attachOwnership = attachOwnership;

    public NativeMetadataResponse Read(NativeMetadataRequest request, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        Validate(request);
        var timeoutMs = ResolveTimeoutMs(request.TimeoutMs);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeoutMs);
        try
        {
            var process = EnsureProcess(request.ScannerPath, deadline.Token);
            using var stopOnCancel = deadline.Token.UnsafeRegister(static state => KillOwnedProcess((Process)state!), process);
            var id = (++_requestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var payload = JsonSerializer.Serialize(new
            {
                type = "metadata", requestId = id, path = request.FilePath,
                readCover = request.ReadCover, coverOutputPath = request.CoverOutputPath,
                allowUntaggedMp3 = request.AllowUntaggedMp3,
            }, ScannerJson);
            process.StandardInput.WriteLineAsync(payload.AsMemory(), deadline.Token).GetAwaiter().GetResult();
            process.StandardInput.FlushAsync(deadline.Token).GetAwaiter().GetResult();
            while (true)
            {
                var response = NativeMetadataProtocol.Response(ReadLine(process, deadline.Token), id);
                if (response is not null) return response;
            }
        }
        catch (Exception error)
        {
            if (error is NativeMetadataReadException { Code: "native_metadata_cleanup_pending" }) throw;
            // A well-formed per-file error reply leaves the scanner healthy: keep it instead of respawning per bad file.
            if (error is NativeMetadataReadException { Code: "native_metadata_failed" } && !_stopping
                && !cancellation.IsCancellationRequested && !deadline.IsCancellationRequested) throw;
            if (!_stopping) Suspend();
            if (_stopping) throw new NativeMetadataReadException("native_metadata_cleanup_unconfirmed");
            if (cancellation.IsCancellationRequested) throw new OperationCanceledException("Native metadata read cancelled.", cancellation);
            if (deadline.IsCancellationRequested) throw new NativeMetadataReadException("native_metadata_timeout");
            if (error is NativeMetadataReadException) throw;
            if (error is JsonException) throw new NativeMetadataReadException("native_metadata_protocol");
            if (error is System.ComponentModel.Win32Exception or FileNotFoundException or DirectoryNotFoundException)
                throw new NativeMetadataReadException("native_metadata_unavailable");
            throw new NativeMetadataReadException("native_metadata_failed");
        }
    }

    public void Suspend()
    {
        var process = _process;
        if (process is null) return;
        _ownership?.Dispose();
        _ownership = null;
        _stopping = !StopOwnedProcess(process);
        if (!_stopping) { _process = null; _scannerPath = null; }
    }

    public void Dispose() => Suspend();

    private Process EnsureProcess(string scannerPath, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_stopping) {
            Suspend();
            if (_stopping) throw new NativeMetadataReadException("native_metadata_cleanup_pending");
        }
        if (_process is not null && _scannerPath == scannerPath && !_process.HasExited) return _process;
        Suspend();
        if (_stopping) throw new NativeMetadataReadException("native_metadata_cleanup_pending");
        var start = new ProcessStartInfo(scannerPath)
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
        };
        var process = Process.Start(start) ?? throw new InvalidOperationException("Native metadata scanner could not start.");
        _process = process;
        _scannerPath = scannerPath;
        var registration = _attachOwnership(process);
        _ownership = registration.Ownership;
        var ownershipError = registration.Error;
        if (ownershipError != 0) {
            // Core crash recovery must never overlap an unprotected scanner still reading borrowed files.
            Suspend();
            throw new NativeMetadataReadException(_stopping ? "native_metadata_cleanup_unconfirmed" : "native_metadata_unavailable");
        }
        process.ErrorDataReceived += static (_, _) => { }; // Drain stderr without logging local paths.
        process.BeginErrorReadLine();
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (Exception) { /* Scheduling priority is best effort across platforms. */ }
        try
        {
            using var stopOnCancel = cancellation.UnsafeRegister(static state => KillOwnedProcess((Process)state!), process);
            NativeMetadataProtocol.RequireEvent(ReadLine(process, cancellation), "capabilities");
            NativeMetadataProtocol.RequireEvent(ReadLine(process, cancellation), "ready");
            _process = process;
            _scannerPath = scannerPath;
            return process;
        }
        catch
        {
            Suspend();
            throw;
        }
    }

    /// <summary>Absent means the legacy 60s budget; a supplied value is clamped so it can never exceed it.</summary>
    internal static int ResolveTimeoutMs(int? requested) =>
        requested is { } value ? Math.Clamp(value, MinTimeoutMs, DefaultTimeoutMs) : DefaultTimeoutMs;

    private static string ReadLine(Process process, CancellationToken cancellation)
    {
        var line = process.StandardOutput.ReadLineAsync(cancellation).GetAwaiter().GetResult()
            ?? throw new NativeMetadataReadException("native_metadata_crashed");
        if (line.Length > MaxLineChars) throw new NativeMetadataReadException("native_metadata_protocol");
        return line;
    }

    private static void KillOwnedProcess(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception) { }
    }

    private static bool StopOwnedProcess(Process process)
    {
        KillOwnedProcess(process);
        try {
            if (process.WaitForExit(1_000)) { process.Dispose(); return true; }
        }
        catch (Exception) { }
        return false;
    }

    private static void Validate(NativeMetadataRequest request)
    {
        if (!Path.IsPathFullyQualified(request.ScannerPath)
            || !Path.IsPathFullyQualified(request.FilePath) || request.FilePath.Length > 4096)
            throw new ArgumentException("Invalid native metadata path.");
        if (!request.ReadCover) return;
        var output = request.CoverOutputPath;
        if (string.IsNullOrEmpty(output) || !Path.IsPathFullyQualified(output)
            || !Path.GetFileName(Path.GetDirectoryName(output) ?? string.Empty).StartsWith("echo-native-metadata-cover-", StringComparison.Ordinal)
            || !Path.GetFileName(output).StartsWith("cover-", StringComparison.Ordinal))
            throw new ArgumentException("Invalid native metadata cover output path.");
    }
}
