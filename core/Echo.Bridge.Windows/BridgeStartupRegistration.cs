using Microsoft.Win32;

namespace Echo.Bridge.Windows;

/// <summary>Current-user login registration; no service, elevation or resident helper.</summary>
internal sealed class BridgeStartupRegistration(string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
{
    private const string ValueName = "ECHO Bridge";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string value
                && value.Length <= 260 && value.EndsWith("\" --startup", StringComparison.Ordinal);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable unavailable.");
        var command = "\"" + executable + "\" --startup";
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"') || command.Length > 260)
            throw new InvalidOperationException("Executable cannot be registered for login.");
        using var run = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        run.SetValue(ValueName, command, RegistryValueKind.String);
    }
}
