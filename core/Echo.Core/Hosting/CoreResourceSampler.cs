using System.Diagnostics;

namespace Echo.Core.Hosting;

public sealed class CoreResourceUsage
{
    public int Pid { get; init; }
    public long WorkingSetBytes { get; init; }
    public long? PrivateBytes { get; init; }
}

internal static class CoreResourceSampler
{
    public const string Capability = "diagnostics.resourceUsage/v1";

    public static CoreResourceUsage? Sample()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            return new CoreResourceUsage
            {
                Pid = process.Id,
                WorkingSetBytes = process.WorkingSet64,
                // Do not equate resident/shared memory with private memory on other platforms.
                PrivateBytes = OperatingSystem.IsWindows() ? process.PrivateMemorySize64 : null,
            };
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
