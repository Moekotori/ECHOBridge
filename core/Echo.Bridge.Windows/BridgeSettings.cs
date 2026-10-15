using System.Text.Json;
using System.Text.RegularExpressions;

namespace Echo.Bridge.Windows;

internal sealed class BridgeSettings
{
    public string? DatabasePath { get; set; }
    public bool UseAutomaticLibrary { get; set; } = true;
    public string DeviceId { get; set; } = "bridge-" + Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = Environment.MachineName + " ECHO Bridge";
    public bool SharingEnabled { get; set; } = true;
    public bool AutomaticUpdates { get; set; } = true;
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECHO Bridge", "bridge-settings.json");

    public static BridgeSettings Load() => LoadFile(FilePath);
    internal static BridgeSettings LoadFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return new();
            if (new FileInfo(filePath).Length > 64 * 1024) return new() { SharingEnabled = false };
            var value = JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllBytes(filePath));
            if (value is null || !Regex.IsMatch(value.DeviceId, "^bridge-[a-f0-9]{32}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(20)))
                return new() { SharingEnabled = false };
            if (string.IsNullOrWhiteSpace(value.DeviceName) || value.DeviceName.Length > 80) value.DeviceName = "ECHO Bridge";
            return value;
        }
        // A damaged existing preference cannot establish consent to resume sharing.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new() { SharingEnabled = false }; }
    }

    public void Save() => SaveFile(FilePath);
    internal void SaveFile(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temporary = filePath + "." + Environment.ProcessId + ".tmp";
        try { File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(this)); File.Move(temporary, filePath, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
