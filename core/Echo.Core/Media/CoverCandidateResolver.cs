using System.Security.Cryptography;
using Echo.Core.Protocol;

namespace Echo.Core.Media;

/// <summary>Read-only cover selection and hashing. Image variants remain in the bounded image worker.</summary>
public sealed class CoverCandidateResolver
{
    public const string Capability = "media.coverCandidate/v1";
    private const long MaxCoverBytes = 20L * 1024 * 1024;
    private const int MaxHashEntries = 128;
    private static readonly string[] Names = ["cover", "folder", "front", "albumart", "album art", "album_art", "front cover", "cover front"];
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly HashSet<string> NameKeys = Names.Select(NormalizeName).ToHashSet(StringComparer.Ordinal);
    private static readonly string[] NamePrefixes = ["cover", "folder", "front", "albumart"];
    private readonly Dictionary<string, (long Bytes, long Ticks, string Hash)> _hashes = new(StringComparer.Ordinal);
    private string? _lastFolder;
    private CoverCandidateResponse? _lastFolderCandidate;

    public void Clear()
    {
        _hashes.Clear();
        _lastFolder = null;
        _lastFolderCandidate = null;
    }

    public CoverCandidateResponse Resolve(CoverCandidateRequest request, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(request.FilePath) || request.FilePath.Length > 4096)
            throw new ArgumentException("Invalid cover source path.");
        if (request.EmbeddedPath is not null)
        {
            if (!Path.IsPathFullyQualified(request.EmbeddedPath) || request.EmbeddedBytes is null
                || request.EmbeddedBytes < 0 || request.EmbeddedMimeType?.Length > 255)
                throw new ArgumentException("Invalid embedded cover source.");
            if (request.EmbeddedBytes > MaxCoverBytes)
                return new("resolved", "default", Warning: $"embedded cover skipped: {request.EmbeddedBytes} bytes exceeds {MaxCoverBytes}");
            return FileCandidate("embedded", request.EmbeddedPath, request.EmbeddedMimeType, cancellation, request.EmbeddedBytes);
        }

        var directory = Path.GetDirectoryName(request.FilePath);
        if (string.IsNullOrEmpty(directory)) return new("resolved", "default");
        if (_lastFolder == directory && _lastFolderCandidate is { } previous)
        {
            if (previous.Source != "folder" || previous.Path is null) return previous;
            var refreshed = FileCandidate("folder", previous.Path, previous.MimeType, cancellation);
            if (refreshed.Status == "resolved" && refreshed.Source == "folder")
                return _lastFolderCandidate = refreshed with { SharedAcrossFolder = true };
        }
        foreach (var name in Names)
            foreach (var extension in Extensions)
            {
                cancellation.ThrowIfCancellationRequested();
                var path = Path.Combine(directory, name + extension);
                if (File.Exists(path)) return Remember(directory, FileCandidate("folder", path, Mime(extension), cancellation) with { SharedAcrossFolder = true });
            }

        // The legacy locale sort has observable tie-breaking. Delegate ambiguous folders to it.
        string? discovered = null;
        bool discoveredShared = false;
        // An image named after some other audio file is a per-track sidecar: it makes the "no cover" answer
        // track-specific, so it must not be shared with the rest of the folder.
        bool sawPerTrackImage = false;
        try
        {
            var audioName = NormalizeName(Path.GetFileNameWithoutExtension(request.FilePath));
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                cancellation.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (!Extensions.Contains(extension, StringComparer.Ordinal)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                var key = NormalizeName(Path.GetFileNameWithoutExtension(path));
                var shared = NameKeys.Contains(key) || NamePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));
                if (!shared
                    && (audioName.Length == 0 || key != audioName)) { sawPerTrackImage = true; continue; }
                if (discovered is not null) return Remember(directory, new("legacy", "default"));
                discovered = path;
                discoveredShared = shared;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Remember(directory, new("legacy", "default")); }
        if (discovered is not null && !discoveredShared) return new("legacy", "default");
        return Remember(directory, discovered is null ? new("resolved", "default", SharedAcrossFolder: !sawPerTrackImage)
            : FileCandidate("folder", discovered, Mime(Path.GetExtension(discovered)), cancellation) with { SharedAcrossFolder = discoveredShared });
    }

    private CoverCandidateResponse Remember(string directory, CoverCandidateResponse candidate)
    {
        _lastFolder = candidate.SharedAcrossFolder ? directory : null;
        _lastFolderCandidate = candidate.SharedAcrossFolder ? candidate : null;
        return candidate;
    }

    private CoverCandidateResponse FileCandidate(string source, string path, string? mimeType, CancellationToken cancellation, long? expectedBytes = null)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.Directory)
                || expectedBytes is not null && file.Length != expectedBytes)
                return new("legacy", "default");
            if (file.Length > MaxCoverBytes)
                return new("resolved", "default", Warning: source == "embedded"
                    ? $"embedded cover skipped: {file.Length} bytes exceeds {MaxCoverBytes}"
                    : $"{path}: sidecar cover skipped: {file.Length} bytes exceeds {MaxCoverBytes}");
            var bytes = file.Length;
            var ticks = file.LastWriteTimeUtc.Ticks;
            var hash = Hash(path, bytes, ticks, cancellation);
            file.Refresh();
            if (!file.Exists || file.Length != bytes || file.LastWriteTimeUtc.Ticks != ticks)
                return new("legacy", "default");
            var modifiedMs = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds();
            return new("resolved", source, path, bytes, modifiedMs, mimeType, hash);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new("legacy", "default"); }
    }

    private string Hash(string path, long bytes, long ticks, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_hashes.TryGetValue(path, out var entry) && entry.Bytes == bytes && entry.Ticks == ticks) return entry.Hash;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var algorithm = SHA256.Create();
        var hash = Convert.ToHexString(algorithm.ComputeHashAsync(stream, cancellation).GetAwaiter().GetResult()).ToLowerInvariant();
        if (_hashes.Count >= MaxHashEntries) _hashes.Clear();
        _hashes[path] = (bytes, ticks, hash);
        return hash;
    }

    private static string NormalizeName(string value) => new(value.ToLowerInvariant().Where(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9').ToArray());

    private static string? Mime(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => null,
    };
}
