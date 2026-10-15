using System.Text.Json;

namespace Echo.Core.Protocol;

public sealed class NativeMetadataRequest
{
    public string ScannerPath { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public bool ReadCover { get; set; } = true;
    /// <summary>Beatmap overlays already own text tags; plain MP3 technical reads may avoid TS recovery.</summary>
    public bool AllowUntaggedMp3 { get; set; }
    public string? CoverOutputPath { get; set; }
    /// <summary>Optional per-request scanner budget; small files fail fast, large ones keep the default.</summary>
    public int? TimeoutMs { get; set; }
}

public sealed record NativeMetadataResponse(string Status, JsonElement? Result = null);
